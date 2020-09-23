﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using UIForia.Elements;
using UIForia.Layout;
using UIForia.Systems;
using UIForia.Util.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UIForia {

    public struct EmValue {

        public float resolvedValue;
        public float previousValue;
        public UIFixedLength styleValue;

    }

    public unsafe class ElementSystem : IDisposable {

        private const int k_MinFreeIndices = 1024;

        private Queue<int> indexQueue; // todo -- make a better queue, inlined single linked list w/ free list that supports bulk insert

        private int idGenerator;

        public ElementTable<ElementMetaInfo> metaTable;
        public ElementTable<HierarchyInfo> hierarchyTable;
        public ElementTable<ElementTraversalInfo> traversalTable;

        public ElementTable<EmValue> emTable;
        
        public UIElement[] instanceTable;

        internal int elementCapacity;
        private byte* backingStore;

        public DataList<ElementId>.Shared enabledElementsThisFrame;
        public DataList<ElementId>.Shared disabledElementsThisFrame;

        public ElementSystem(int initialElementCapacity) {
            this.idGenerator = 1; // 0 is always invalid
            this.indexQueue = new Queue<int>(k_MinFreeIndices * 2);
            ResizeBackingBuffer(initialElementCapacity);

            // todo -- allocate from same buffer w/ front-back load config (grow one size from top, other from bottom)
            enabledElementsThisFrame = new DataList<ElementId>.Shared(64, Allocator.Persistent);
            disabledElementsThisFrame = new DataList<ElementId>.Shared(32, Allocator.Persistent);
        }

        public void FilterEnabledDisabledElements() {
            new FilterEnabledDisabledElementsJob() {
                metaTable = metaTable,
                enabledElements = enabledElementsThisFrame,
                disabledElements = disabledElementsThisFrame
            }.Run();
        }

        public void Reset() {
            idGenerator = 1;
            indexQueue.Clear();
            enabledElementsThisFrame.size = 0;
            disabledElementsThisFrame.size = 0;
            Array.Clear(instanceTable, 0, instanceTable.Length);
            TypedUnsafe.MemClear(metaTable.array, elementCapacity);
            TypedUnsafe.MemClear(traversalTable.array, elementCapacity);
            TypedUnsafe.MemClear(hierarchyTable.array, elementCapacity);
        }

        // I'll have to see how this gets used. Ideally we push out a lot of indices at once and bulk create
        internal ElementId CreateElement(UIElement element, int depth, int templateId, int templateOriginId, UIElementFlags flags) {
            int idx;
            if (indexQueue.Count >= k_MinFreeIndices) {
                idx = indexQueue.Dequeue();
            }
            else {
                idx = idGenerator++;
                if (idx >= elementCapacity) {
                    ResizeBackingBuffer(elementCapacity * 2);
                }
            }

            // note: need to use generation from existing data at idx
            metaTable.array[idx].flags = flags;
            metaTable.array[idx].__padding__ = 0;

            traversalTable.array[idx] = new ElementTraversalInfo() {
                depth = depth,
                btfIndex = 0,
                ftbIndex = 0,
                //templateId =  templateId,
                //templateOriginId = (ushort) templateOriginId
            };

            hierarchyTable.array[idx] = default;
            instanceTable[idx] = element;

            return new ElementId(idx, metaTable.array[idx].generation);

        }

        public void AddChild(ElementId parentId, ElementId childId) {

            ref HierarchyInfo parentInfo = ref hierarchyTable.array[parentId.index];
            ref HierarchyInfo childInfo = ref hierarchyTable.array[childId.index];

            childInfo.parentId = parentId;
            childInfo.viewId = parentInfo.viewId;
            
            if (parentInfo.childCount == 0) {
                parentInfo.firstChildId = childId;
                parentInfo.lastChildId = childId;
                childInfo.nextSiblingId = default;
                childInfo.prevSiblingId = default;
            }
            else {

                ref HierarchyInfo prevSibling = ref hierarchyTable.array[parentInfo.lastChildId.index];

                childInfo.prevSiblingId = parentInfo.lastChildId;
                childInfo.nextSiblingId = default;
                parentInfo.lastChildId = childId;
                prevSibling.nextSiblingId = childId;
            }

            parentInfo.childCount++;

        }

        public void RemoveChild(ElementId parentId, ElementId childId) {

            ref HierarchyInfo parentInfo = ref hierarchyTable.array[parentId.index];
            ref HierarchyInfo childInfo = ref hierarchyTable.array[childId.index];

            if (childInfo.parentId != parentId) {
                return;
            }

            if (childId == parentInfo.firstChildId) {
                parentInfo.firstChildId = childInfo.nextSiblingId;
            }

            if (childId == parentInfo.lastChildId) {
                parentInfo.lastChildId = childInfo.prevSiblingId;
            }

            if (childInfo.prevSiblingId != default) {
                ref HierarchyInfo prevSibling = ref hierarchyTable.array[parentInfo.lastChildId.index];
                prevSibling.nextSiblingId = childInfo.nextSiblingId;
            }

            if (childInfo.nextSiblingId != default) {
                ref HierarchyInfo nextSibling = ref hierarchyTable.array[parentInfo.lastChildId.index];
                nextSibling.prevSiblingId = childInfo.prevSiblingId;
            }

            // todo -- if we decide sibling index is a good thing we need to update the ids
            // I don't expect lots of add / removal thrashing
            // unless sibling index also updates on enable | disable

            childInfo.prevSiblingId = default;
            childInfo.nextSiblingId = default;
            parentInfo.childCount--;
        }

        public void DestroyElement(ElementId elementId, bool unlinkFromParent) {
            ref ElementMetaInfo meta = ref metaTable.array[elementId.index];
            meta.generation++;
            // meta.flags = default;
            // meta.__padding__ = default;
            indexQueue.Enqueue(elementId.index);
            if (unlinkFromParent) {
                UnlinkFromParent(elementId);
            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAlive(ElementId elementId) {
            return metaTable.array[elementId.index].generation == elementId.generation;
        }

        public bool IsEnabled(ElementId elementId) {
            return metaTable.array[elementId.index].generation == elementId.generation && (metaTable.array[elementId.index].flags & UIElementFlags.EnabledFlagSet) == UIElementFlags.EnabledFlagSet;
        }

        public static bool IsEnabled(ElementId elementId, ElementTable<ElementMetaInfo> metaTable) {
            return metaTable.array[elementId.index].generation == elementId.generation && (metaTable.array[elementId.index].flags & UIElementFlags.EnabledFlagSet) == UIElementFlags.EnabledFlagSet;
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAlive(ElementId elementId, in ElementTable<ElementMetaInfo> metaTable) {
            return metaTable.array[elementId.index].generation == elementId.generation;
        }

        public static bool IsDeadOrDisabled(ElementId elementId, ElementTable<ElementMetaInfo> metaTable) {
            return metaTable.array[elementId.id & ElementId.ENTITY_INDEX_MASK].generation != elementId.generation || (metaTable.array[elementId.id & ElementId.ENTITY_INDEX_MASK].flags & UIElementFlags.EnabledFlagSet) != UIElementFlags.EnabledFlagSet;
        }

        private void UnlinkFromParent(ElementId elementId) {
            // Unlink
            ref HierarchyInfo hierarchyInfo = ref hierarchyTable[elementId];
            ElementId parentId = hierarchyInfo.parentId;
            if (parentId == default) {
                return;
            }
            ref HierarchyInfo parentInfo = ref hierarchyTable[parentId];

            if (parentInfo.firstChildId == elementId) {
                parentInfo.firstChildId = hierarchyInfo.nextSiblingId;
            }

            if (parentInfo.lastChildId == elementId) {
                parentInfo.lastChildId = hierarchyInfo.prevSiblingId;
            }

            if (hierarchyInfo.nextSiblingId != default) {
                hierarchyTable[hierarchyInfo.nextSiblingId].prevSiblingId = hierarchyInfo.prevSiblingId;
            }

            if (hierarchyInfo.prevSiblingId != default) {
                hierarchyTable[hierarchyInfo.prevSiblingId].nextSiblingId = hierarchyInfo.nextSiblingId;
            }

            parentInfo.childCount--;
        }

        public int SetSiblingIndex(ElementId id, int value) {
            // ref HierarchyInfo hierarchyInfo = ref hierarchyTable[id];
            // ElementId parentId = hierarchyInfo.parentId;
            // ref HierarchyInfo parentInfo = ref hierarchyTable[parentId];
            //
            // if (value < 0) value = 0;
            // if (value >= parentInfo.childCount) {
            //     value = parentInfo.childCount - 1;
            // }
            //

            // ElementId ptr = parentInfo.firstChildId;
            //
            // int idx = 0;
            // while (idx != value) {
            //     ptr = hierarchyTable[];
            //     idx++;
            // }
            //
            // // Find location
            // // Insert

            return value;

        }

        public void Dispose() {
            if (backingStore != null) {
                UnsafeUtility.Free(backingStore, Allocator.Persistent);
                backingStore = null;
            }

            enabledElementsThisFrame.Dispose();
            disabledElementsThisFrame.Dispose();
        }

        private void ResizeBackingBuffer(int newCapacity) {

            backingStore = TypedUnsafe.ResizeSplitBuffer(
                ref metaTable.array,
                ref traversalTable.array,
                ref hierarchyTable.array,
                ref emTable.array,
                elementCapacity,
                newCapacity,
                Allocator.Persistent,
                true
            );

            elementCapacity = newCapacity;
            if (instanceTable == null) {
                instanceTable = new UIElement[elementCapacity];
            }
            else {
                if (instanceTable.Length <= elementCapacity) {
                    Array.Resize(ref instanceTable, elementCapacity);
                }
            }

        }

        public int RemoveDeadElements(ElementId* ptr, int size) {
            for (int i = 0; i < size; i++) {
                if (!IsAlive(ptr[i], metaTable)) {
                    ptr[i--] = ptr[--size];
                }
            }

            return size;
        }

        public static int RemoveDeadElements(ElementTable<ElementMetaInfo> metaTable, ElementId* ptr, int size) {

            for (int i = 0; i < size; i++) {
                if (!IsAlive(ptr[i], metaTable)) {
                    ptr[i--] = ptr[--size];
                }
            }

            return size;
        }

        public void CleanupFrame() {

            for (int i = 0; i < enabledElementsThisFrame.size; i++) {
                metaTable[enabledElementsThisFrame[i]].flags &= ~(UIElementFlags.EnableStateChanged | UIElementFlags.EnabledRoot);
            }

            for (int i = 0; i < disabledElementsThisFrame.size; i++) {
                metaTable[disabledElementsThisFrame[i]].flags &= ~(UIElementFlags.DisableRoot);
            }

            enabledElementsThisFrame.size = 0;
            disabledElementsThisFrame.size = 0;
        }

        public static bool IsLaterInHierarchy(ElementId first, ElementId second, ElementTable<ElementTraversalInfo> traversalTable) {
            return traversalTable.array[first.index].ftbIndex > traversalTable.array[second.index].ftbIndex;
        }

        public static ElementId FindFirstEnabledChild(ElementId elementId, ElementTable<ElementMetaInfo> metaTable, ElementTable<HierarchyInfo> elementHierarchyTable) {
            ref HierarchyInfo hierarchyInfo = ref elementHierarchyTable[elementId];

            if (hierarchyInfo.childCount == 0) {
                return default;
            }

            ElementId ptr = elementHierarchyTable[elementId].firstChildId;

            while (ptr != default) {
                if (IsEnabled(ptr, metaTable)) {
                    return ptr;
                }

                ptr = elementHierarchyTable[ptr].nextSiblingId;
            }

            return default;

        }

        public static ElementId FindLastEnabledChild(ElementId elementId, ElementTable<ElementMetaInfo> metaTable, ElementTable<HierarchyInfo> elementHierarchyTable) {
            ref HierarchyInfo hierarchyInfo = ref elementHierarchyTable[elementId];

            if (hierarchyInfo.childCount == 0) {
                return default;
            }

            ElementId ptr = elementHierarchyTable[elementId].lastChildId;

            while (ptr != default) {
                if (IsEnabled(ptr, metaTable)) {
                    return ptr;
                }

                ptr = elementHierarchyTable[ptr].prevSiblingId;
            }

            return default;

        }

        public void ReorderChildren(ElementId parent, IList<IElementIdProvider> lastFrameChildren) {

            for (int i = 0; i < lastFrameChildren.Count; i++) {
                ref HierarchyInfo hierarchyInfo = ref hierarchyTable[lastFrameChildren[i].ElementId];
                if (hierarchyInfo.parentId != parent) {
                    continue;
                }
            }

        }

    }

    public interface IElementIdProvider {

        ElementId ElementId { get; }

    }

}