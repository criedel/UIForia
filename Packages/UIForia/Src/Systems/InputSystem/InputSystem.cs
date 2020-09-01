using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UIForia.Elements;
using UIForia.Rendering;
using UIForia.Systems.Input;
using UIForia.UIInput;
using UIForia.Util;
using Unity.Collections;
using UnityEngine;
using Debug = System.Diagnostics.Debug;

namespace UIForia.Systems {

    public abstract class InputSystem : IInputSystem {

        public event Action<IFocusable> onFocusChanged;

        private const float k_DragThreshold = 5f;
    private ElementComparer elementComp;

        private readonly LayoutSystem layoutSystem;

        protected readonly KeyboardInputManager keyboardInputManager;

        private List<UIElement> m_ElementsThisFrame;

#if UNITY_EDITOR
        public List<UIElement> DebugElementsThisFrame => m_ElementsLastFrame;
        public bool DebugMouseUpThisFrame => mouseState.isLeftMouseUpThisFrame;
#endif

        private List<UIElement> m_ElementsLastFrame;

        // temporary hack for the building system, this should be formalized and use ElementRef instead
        public IReadOnlyList<UIElement> ElementsThisFrame => m_ElementsLastFrame;

        private CursorStyle currentCursor;

        protected UIElement m_FocusedElement;
        protected DragEvent currentDragEvent;

        protected MouseState mouseState;
        protected KeyboardInputState m_KeyboardState;

        private readonly List<UIElement> m_ExitedElements;
        private readonly List<UIElement> m_ActiveElements;
        private readonly List<UIElement> m_EnteredElements;
        private readonly LightList<UIElement> m_MouseDownElements;
        private readonly LightList<UIElement> hoveredElements;

        private readonly EventPropagator m_EventPropagator;
        private readonly List<ValueTuple<object, UIElement>> m_MouseEventCaptureList;

        public KeyboardModifiers KeyboardModifiers => m_KeyboardState.modifiersThisFrame;

        private readonly SkipTree<UIElement> m_KeyboardEventTree;

        private LightList<KeyboardEventHandlerInvocation>
            lateHandlers = new LightList<KeyboardEventHandlerInvocation>();

        private LightList<UIEvent> lateTriggers = new LightList<UIEvent>();

        private List<IFocusable> focusables;

        private int focusableIndex;

        protected InputSystem(LayoutSystem layoutSystem, KeyboardInputManager keyboardInputManager = null) {
            this.layoutSystem = layoutSystem;

            this.m_MouseDownElements = new LightList<UIElement>();
            this.m_ElementsThisFrame = new List<UIElement>();
            this.m_ElementsLastFrame = new List<UIElement>();
            this.m_EnteredElements = new List<UIElement>();
            this.m_ExitedElements = new List<UIElement>();
            this.m_ActiveElements = new List<UIElement>();
            this.elementComp = new ElementComparer();
            
            this.m_KeyboardEventTree = new SkipTree<UIElement>();
            this.keyboardInputManager = keyboardInputManager ?? new KeyboardInputManager();
            this.m_EventPropagator = new EventPropagator();
            this.m_MouseEventCaptureList = new List<ValueTuple<object, UIElement>>();
            // this.m_DragEventCaptureList = new List<ValueTuple<DragEventHandler, UIElement>>();
            this.m_FocusedElement = null;
            this.focusables = new List<IFocusable>();
            this.hoveredElements = new LightList<UIElement>(16);
        }

        public DragEvent CurrentDragEvent => currentDragEvent;

        public bool IsMouseLeftDown => mouseState.isLeftMouseDown;
        public bool IsMouseLeftDownThisFrame => mouseState.isLeftMouseDownThisFrame;
        public bool IsMouseLeftUpThisFrame => mouseState.isLeftMouseUpThisFrame;

        public bool IsMouseRightDown => mouseState.isRightMouseDown;
        public bool IsMouseRightDownThisFrame => mouseState.isRightMouseDownThisFrame;
        public bool IsMouseRightUpThisFrame => mouseState.isRightMouseUpThisFrame;

        public bool IsMouseMiddleDown => mouseState.isMiddleMouseDown;
        public bool IsMouseMiddleDownThisFrame => mouseState.isMiddleMouseDownThisFrame;
        public bool IsMouseMiddleUpThisFrame => mouseState.isMiddleMouseUpThisFrame;

        public Vector2 ScrollDelta => mouseState.scrollDelta;

        public Vector2 MousePosition => mouseState.mousePosition;
        public Vector2 MouseDownPosition => mouseState.leftMouseButtonState.downPosition;

        public bool IsDragging { get; protected set; }

        protected abstract MouseState GetMouseState();

        public IFocusable GetFocusedElement() {
            return (IFocusable) m_FocusedElement;
        }

        public void RegisterFocusable(IFocusable focusable) {
            focusables.Add(focusable);
        }

        public void UnRegisterFocusable(IFocusable focusable) {
            focusables.Remove(focusable);
        }

        public void FocusNext() {
            int initialIndex = focusableIndex;
            do {
                focusableIndex = focusableIndex == 0 ? focusables.Count - 1 : focusableIndex - 1;
            } while (!(RequestFocus(focusables[focusableIndex]) || focusableIndex == initialIndex));
        }

        public void FocusPrevious() {
            int initialIndex = focusableIndex;
            do {
                focusableIndex = focusableIndex + 1 == focusables.Count ? 0 : focusableIndex + 1;
            } while (!(RequestFocus(focusables[focusableIndex]) || focusableIndex == initialIndex));
        }

        public bool RequestFocus(IFocusable target) {
            if (!(target is UIElement element && !element.isDisabled)) {
                return false;
            }

            // try focus the element early to see if they accept being focused.
            if (!target.Focus()) {
                return false;
            }

            // todo -- if focus handlers added via template invoke them
            if (m_FocusedElement != null) {
                if (m_FocusedElement == (UIElement) target) {
                    return true;
                }

                IFocusable focusable = (IFocusable) m_FocusedElement;
                m_FocusedElement.style.ExitState(StyleState.Focused);
                focusable.Blur();
                onFocusChanged?.Invoke(target);
            }

            m_FocusedElement = (UIElement) target;
            m_FocusedElement.style.EnterState(StyleState.Focused);
            onFocusChanged?.Invoke(target);
            focusableIndex = -1;
            for (int i = 0; i < focusables.Count; i++) {
                if (focusables[i] == target) {
                    focusableIndex = i;
                    return true;
                }
            }

            return true;
        }

        public void ReleaseFocus(IFocusable target) {
            if (m_FocusedElement.isDisabled || m_FocusedElement.isDestroyed) {
                m_FocusedElement = null;
                focusableIndex = -1;
                onFocusChanged?.Invoke(target);
                return;
            }

            if (m_FocusedElement == (UIElement) target) {
                IFocusable focusable = (IFocusable) m_FocusedElement;
                m_FocusedElement.style.ExitState(StyleState.Focused);
                focusable.Blur();
                // todo -- if focus handlers added via template invoke them
                m_FocusedElement = null;
                focusableIndex = -1;
                onFocusChanged?.Invoke(target);
            }
        }

        public void Read() {
            mouseState = GetMouseState();
        }

        private void RunBindings(UIElement element) {
            UIElement start = element;

            LightList<UIElement> tmp = LightList<UIElement>.Get();

            UIElement ptr = start;
            while (ptr != null) {
                tmp.Add(ptr);
                ptr = ptr.parent;
            }

            for (int i = tmp.size - 1; i >= 0; i--) {
                LinqBindingNode bindingNode = tmp[i].bindingNode;
                bindingNode?.updateBindings?.Invoke(bindingNode.root, bindingNode.element);
                // UnityEngine.Debug.Log($"{new string(' ', bindingNode.element.hierarchyDepth * 4)}pre-binding" + bindingNode.element.GetDisplayName());
            }

            tmp.Release();
        }

        private void RunWriteBindings(UIElement element) {
            UIElement start = element;

            LightList<UIElement> tmp = LightList<UIElement>.Get();

            UIElement ptr = start;
            while (ptr != null) {
                tmp.Add(ptr);
                ptr = ptr.parent;
            }

            for (int i = 0; i < tmp.size; i++) {
                LinqBindingNode bindingNode = tmp[i].bindingNode;
                bindingNode?.lateBindings?.Invoke(bindingNode.root, bindingNode.element);
                // UnityEngine.Debug.Log($"{new string(' ', bindingNode.element.hierarchyDepth * 4)}pre-binding" + bindingNode.element.GetDisplayName());
            }

            tmp.Release();
        }

        public virtual void OnUpdate() {
            m_KeyboardState = keyboardInputManager.UpdateKeyboardInputState();

            ProcessKeyboardEvents();
            ProcessMouseInput();
            UIElement firstElement = null;
            if (m_ElementsThisFrame.Count != 0) {
                firstElement = m_ElementsThisFrame[0];

                RunBindings(firstElement);
            }

            if (!IsDragging) {
                ProcessMouseEvents();
            }
            else {
                RunMouseEvents(m_ExitedElements, InputEventType.MouseExit);
            }

            ProcessDragEvents();

            if (firstElement != null) {
                RunWriteBindings(firstElement);
            }

            List<UIElement> temp = m_ElementsLastFrame;
            m_ElementsLastFrame = m_ElementsThisFrame;
            m_ElementsThisFrame = temp;

            for (int i = 0; i < m_ElementsLastFrame.Count; i++) {
                if (m_ElementsLastFrame[i].isDisabled || m_ElementsLastFrame[i].isDestroyed) {
                    m_ElementsLastFrame.RemoveAt(i--);
                }
            }

            m_ElementsThisFrame.Clear();
            m_EnteredElements.Clear();
            m_ExitedElements.Clear();

            if (IsMouseLeftUpThisFrame) {
                m_MouseDownElements.Clear();
            }
        }

        public virtual void DelayEvent(UIElement origin, UIEvent evt) {
            evt.origin = origin;
            lateTriggers.Add(evt);
        }

        private void ProcessKeyboardEvents() {
            StructList<KeyCodeState> keyCodeStates = m_KeyboardState.GetKeyCodeStates();
            for (int i = 0; i < keyCodeStates.size; i++) {
                KeyCodeState keyCodeState = keyCodeStates[i];

                InputEventType inputEventType;
                if (keyCodeState.keyState == KeyState.DownThisFrame) {
                    inputEventType = InputEventType.KeyDown;
                }
                else if (keyCodeState.keyState == KeyState.Down) {
                    inputEventType = InputEventType.KeyHeldDown;
                }
                else {
                    inputEventType = InputEventType.KeyUp;
                }

                ProcessKeyboardEvent(keyCodeState.keyCode, inputEventType, keyCodeState.character, m_KeyboardState.modifiersThisFrame);
            }
        }

        private LightList<UIElement> ancestorBuffer = new LightList<UIElement>();
        private LightList<UIElement> queryResults = new LightList<UIElement>();

        private void ProcessMouseInput() {
            // if element does not have state requested -> hover flag, drag listener, pointer events = none, don't add
            // buckets feel like a lot of overhead
            // for each element, track if has overflowing children 
            // if it does not and element is culled, skip directly to children's children and repeat
            // if aabb yMin is below screen height or aabb ymax is less than 0 -> cull

            // broadphase culling and input querying are related
            // neither uses render bounds, just obb and aabb
            // if dragging only attempt intersections with elements who have drag responders
            // if not dragging only attempt intersections with elements who have hover state (if mouse is present) or drag create or mouse / touch interactions

            queryResults.QuickClear();

            layoutSystem.QueryPoint(mouseState.mousePosition, queryResults);

            if (UnityEngine.Input.GetKeyDown(KeyCode.A)) {
                Debugger.Break();
            }

            ElementTable<ElementTraversalInfo> traversalTable = layoutSystem.elementSystem.traversalTable;

            // queryResults.Sort((a, b) => traversalTable[b.id].ftbIndex - traversalTable[a.id].ftbIndex);
            elementComp.traversalTable = traversalTable;
            queryResults.Sort(elementComp);

            if (!IsDragging) {
                ancestorBuffer.QuickClear();

                if (queryResults.size > 0) {
                    /*
                     * Every following element must be a parent of the first.
                     * This makes no sense for drag events but a lot for every other.
                     */
                    UIElement firstElement = queryResults[0];
                    ancestorBuffer.Add(firstElement);

                    for (int index = 1; index < queryResults.size; index++) {
                        UIElement element = queryResults[index];
                        if (traversalTable[element.id].IsAncestorOf(traversalTable[firstElement.id])) {
                            ancestorBuffer.Add(element);
                        }
                    }

                    queryResults.size = 0;
                    queryResults.AddRange(ancestorBuffer);
                }
            }

            bool didMouseMove = mouseState.DidMove;

            if (didMouseMove) {
                for (int i = 0; i < hoveredElements.size; i++) {
                    UIElement element = hoveredElements.array[i];

                    if (!element.isEnabled) {
                        hoveredElements.RemoveAt(i--);
                        continue;
                    }

                    if (!queryResults.Contains(element)) {
                        hoveredElements.RemoveAt(i--);
                        element.style.ExitState(StyleState.Hover);
                    }
                }

                for (int i = 0; i < queryResults.Count; i++) {
                    UIElement element = queryResults.array[i];

                    if ((element.style.currentState & StyleState.Hover) == 0) {
                        hoveredElements.Add(element);
                        element.style.EnterState(StyleState.Hover);
                    }
                }
            }

            for (int i = 0; i < queryResults.Count; i++) {
                UIElement element = queryResults[i];

                m_ElementsThisFrame.Add(element);

                if (!m_ElementsLastFrame.Contains(element)) {
                    m_EnteredElements.Add(element);
                }

                if (IsMouseLeftDownThisFrame) {
                    element.style?.EnterState(StyleState.Active);
                    m_ActiveElements.Add(element);
                }
            }

            for (int i = 0; i < m_ElementsLastFrame.Count; i++) {
                if (!m_ElementsThisFrame.Contains(m_ElementsLastFrame[i])) {
                    m_ExitedElements.Add(m_ElementsLastFrame[i]);
                }
            }

            if (IsMouseLeftUpThisFrame) {
                for (int i = 0; i < m_ActiveElements.Count; i++) {
                    m_ActiveElements[i].style?.ExitState(StyleState.Active);
                }

                m_ActiveElements.Clear();
            }

            if (!IsDragging) {
                CursorStyle newCursor = null;
                if (m_ElementsThisFrame.Count > 0) {
                    for (int i = 0; i < m_ElementsThisFrame.Count; i++) {
                        UIElement element = m_ElementsThisFrame[i];

                        if (element.style.IsDefined(StylePropertyId.Cursor)) {
                            newCursor = element.style.Cursor;
                            if (!newCursor.Equals(currentCursor)) {
                                Cursor.SetCursor(newCursor.texture, newCursor.hotSpot, CursorMode.Auto);
                            }

                            break;
                        }
                    }
                }

                if (currentCursor != null && newCursor == null) {
                    Cursor.SetCursor(null, new Vector2(0, 0), CursorMode.Auto);
                }

                currentCursor = newCursor;

                if (mouseState.AnyMouseDownThisFrame) {
                    m_MouseDownElements.AddRange(m_ElementsThisFrame);
                }
            }
        }

        private static bool IsParentOf(UIElement element, UIElement child) {
            UIElement ptr = child.parent;
            while (ptr != null) {
                if (ptr == element) {
                    return true;
                }

                ptr = ptr.parent;
            }

            return false;
        }

        private void ProcessDragEvents() {
            if (IsDragging) {
                if (mouseState.ReleasedDrag) {
                    EndDrag(InputEventType.DragDrop);
                    m_MouseDownElements.Clear();
                }
                else {
                    UpdateDrag();
                }
            }
            else if (mouseState.AnyMouseDown) {
                if (Vector2.Distance(mouseState.MouseDownPosition, mouseState.mousePosition) >= k_DragThreshold / Application.dpiScaleFactor) {
                    BeginDrag();
                }
            }
        }

        private void UpdateDrag(bool firstFrame = false) {
            if (currentDragEvent == null) {
                return;
            }

            if (currentDragEvent.lockCursor && currentDragEvent.cursor != null) {
                Cursor.SetCursor(currentDragEvent.cursor.texture, currentDragEvent.cursor.hotSpot, CursorMode.Auto);
                currentCursor = currentDragEvent.cursor;
            }

            currentDragEvent.MousePosition = MousePosition;
            currentDragEvent.Modifiers = m_KeyboardState.modifiersThisFrame;

            if (firstFrame) {
                RunDragEvent(m_ElementsThisFrame, InputEventType.DragEnter);
                currentDragEvent.Update();
            }
            else {
                RunDragEvent(m_ExitedElements, InputEventType.DragExit);
                RunDragEvent(m_EnteredElements, InputEventType.DragEnter);
                currentDragEvent.Update();
                RunDragEvent(m_ElementsThisFrame,
                    mouseState.DidMove ? InputEventType.DragMove : InputEventType.DragHover);
            }

            if (currentDragEvent.IsCanceled) {
                EndDrag(InputEventType.DragCancel);
            }

            if (currentDragEvent.IsDropped) {
                EndDrag(InputEventType.DragDrop);
            }
        }

        private void BeginDrag() {
            if (currentDragEvent != null) {
                return;
            }

            if (m_MouseDownElements.size == 0) {
                return;
            }

            mouseState.leftMouseButtonState.isDrag = mouseState.isLeftMouseDown;
            mouseState.rightMouseButtonState.isDrag = mouseState.isRightMouseDown;
            mouseState.middleMouseButtonState.isDrag = mouseState.isMiddleMouseDown;

            IsDragging = true;
            m_EventPropagator.Reset(mouseState);

            m_EventPropagator.origin = m_MouseDownElements.array[0];

            for (int i = 0; i < m_MouseDownElements.Count; i++) {
                UIElement element = m_MouseDownElements[i];

                if (element.isDestroyed || element.isDisabled || element.inputHandlers == null) {
                    continue;
                }

                if ((element.inputHandlers.handledEvents & InputEventType.DragCreate) == 0) {
                    continue;
                }

                for (int creatorIndex = 0; creatorIndex < element.inputHandlers.dragCreators.size; creatorIndex++) {
                    InputHandlerGroup.DragCreatorData data = element.inputHandlers.dragCreators.array[creatorIndex];

                    currentDragEvent = data.handler.Invoke(new MouseInputEvent(m_EventPropagator, InputEventType.DragCreate, m_KeyboardState.modifiersThisFrame, false, element));

                    if (currentDragEvent != null) {
                        currentDragEvent.StartTime = Time.realtimeSinceStartup;
                        currentDragEvent.DragStartPosition = MousePosition;
                        currentDragEvent.origin = element;
                        currentDragEvent.Begin();
                        UpdateDrag(true);
                        return;
                    }
                }
            }

            if (currentDragEvent == null) {
                IsDragging = false;
            }

            // todo -- capture phase
        }

        private void EndDrag(InputEventType evtType) {
            IsDragging = false;

            if (currentDragEvent == null) {
                return;
            }

            currentDragEvent.MousePosition = MousePosition;
            currentDragEvent.Modifiers = m_KeyboardState.modifiersThisFrame;

            bool isOriginElementThisFrame = false;
            for (int i = 0; i < m_ElementsThisFrame.Count; i++) {
                if (m_ElementsThisFrame[i].id == currentDragEvent.origin.id) {
                    isOriginElementThisFrame = true;
                    break;
                }
            }

            if (!isOriginElementThisFrame) {
                m_ElementsThisFrame.Add(currentDragEvent.origin);
            }

            if (evtType == InputEventType.DragCancel) {
                RunDragEvent(m_ElementsThisFrame, InputEventType.DragCancel);
                currentDragEvent.Cancel();
            }
            else if (evtType == InputEventType.DragDrop) {
                RunDragEvent(m_ElementsThisFrame, InputEventType.DragDrop);
                currentDragEvent.Drop(true);
            }

            currentDragEvent.OnComplete();
            currentDragEvent = null;
        }

        private void RunDragEvent(List<UIElement> elements, InputEventType eventType) {
            if (currentDragEvent.IsCanceled && eventType != InputEventType.DragCancel) {
                return;
            }

            currentDragEvent.CurrentEventType = eventType;
            currentDragEvent.source = m_EventPropagator;

            m_EventPropagator.Reset(mouseState);

            LightList<Action<DragEvent>> captureList = LightList<Action<DragEvent>>.Get();

            for (int i = 0; i < elements.Count; i++) {
                UIElement element = elements[i];

                if (element.isDestroyed || element.isDisabled) {
                    continue;
                }

                if (element.inputHandlers == null) {
                    continue;
                }

                if ((element.inputHandlers.handledEvents & eventType) == 0) {
                    continue;
                }

                for (int j = 0; j < element.inputHandlers.eventHandlers.size; j++) {
                    ref InputHandlerGroup.HandlerData handler = ref element.inputHandlers.eventHandlers.array[j];

                    if ((handler.eventType & eventType) == 0) {
                        continue;
                    }

                    Action<DragEvent> castHandler = (Action<DragEvent>) handler.handlerFn;

                    if (handler.eventPhase != EventPhase.Bubble) {
                        captureList.Add(castHandler);
                        continue;
                    }

                    CurrentDragEvent.element = element;
                    castHandler.Invoke(currentDragEvent);

                    if (currentDragEvent.IsCanceled || m_EventPropagator.shouldStopPropagation) {
                        break;
                    }
                }

                if (currentDragEvent.IsCanceled || m_EventPropagator.shouldStopPropagation) {
                    captureList.Release();
                    return;
                }
            }

            for (int i = 0; i < captureList.size; i++) {
                if (currentDragEvent.IsCanceled || m_EventPropagator.shouldStopPropagation) {
                    break;
                }

                captureList.array[i].Invoke(currentDragEvent);
            }

            captureList.Release();
        }

        public void OnReset() {
            // don't clear key states
            m_FocusedElement = null;

            focusables.Clear();

            focusableIndex = -1;

            m_ElementsLastFrame.Clear();

            m_ElementsThisFrame.Clear();

            m_MouseDownElements.Clear();

            m_KeyboardEventTree.Clear();

            currentDragEvent = null;

            IsDragging = false;
        }

        public void OnDestroy() { }

        public void OnViewAdded(UIView view) { }

        public void OnViewRemoved(UIView view) { }

        public void OnElementEnabled(UIElement element) { }

        public void OnElementDisabled(UIElement element) {
            BlurOnDisableOrDestroy();
        }

        public void OnElementDestroyed(UIElement element) {
            BlurOnDisableOrDestroy();

            m_ElementsLastFrame.Remove(element);

            m_ElementsThisFrame.Remove(element);

            m_MouseDownElements.Remove(element);

            m_KeyboardEventTree.RemoveHierarchy(element);
        }

        internal void BlurOnDisableOrDestroy() {
            if (m_FocusedElement != null && (m_FocusedElement.isDisabled || m_FocusedElement.isDestroyed)) {
                try {
                    ReleaseFocus((IFocusable) m_FocusedElement);
                }
                catch (Exception e) {
                    UnityEngine.Debug.LogException(e);
                }
            }
        }

        public void OnElementCreated(UIElement element) { }

        public void OnAttributeSet(UIElement element, string attributeName, string currentValue,
            string attributeValue) { }

        public bool IsKeyDown(KeyCode keyCode) {
            return m_KeyboardState.IsKeyDown(keyCode);
        }

        public bool IsKeyDownThisFrame(KeyCode keyCode) {
            return m_KeyboardState.IsKeyDownThisFrame(keyCode);
        }

        public bool IsKeyUp(KeyCode keyCode) {
            return m_KeyboardState.IsKeyUp(keyCode);
        }

        public bool IsKeyUpThisFrame(KeyCode keyCode) {
            return m_KeyboardState.IsKeyUpThisFrame(keyCode);
        }

        public KeyState GetKeyState(KeyCode keyCode) {
            return m_KeyboardState.GetKeyState(keyCode);
        }

        public void RegisterKeyboardHandler(UIElement element) {
            const InputEventType keyEvents = InputEventType.KeyDown | InputEventType.KeyUp | InputEventType.KeyHeldDown;
            if (element.inputHandlers != null && (element.inputHandlers.handledEvents & keyEvents) != 0) {
                m_KeyboardEventTree.AddItem(element);
            }
        }

        protected void ProcessKeyboardEvent(KeyCode keyCode, InputEventType eventType, char character, KeyboardModifiers modifiers) {
            // GenericInputEvent keyEvent = new GenericInputEvent(eventType, modifiers, m_EventPropagator, character, keyCode, m_FocusedElement != null);
            KeyboardInputEvent keyInputEvent = new KeyboardInputEvent(eventType, keyCode, character, modifiers, m_FocusedElement != null);
            if (m_FocusedElement == null) {
                m_KeyboardEventTree.ConditionalTraversePreOrder(keyInputEvent, (item, evt) => {
                    if (evt.stopPropagation) return false;

                    UIElement element = (UIElement) item.Element;
                    if (element.isDestroyed || element.isDisabled) {
                        return false;
                    }

                    InputHandlerGroup evtHandlerGroup = item.inputHandlers;

                    bool ran = false;
                    for (int i = 0; i < evtHandlerGroup.eventHandlers.size; i++) {
                        if (evt.stopPropagation) break;
                        ref InputHandlerGroup.HandlerData handler = ref evtHandlerGroup.eventHandlers.array[i];
                        if (!ShouldRun(handler, evt)) {
                            continue;
                        }

                        if (!ran) {
                            ran = true;
                            RunBindings(element);
                        }

                        Action<KeyboardInputEvent> keyHandler = handler.handlerFn as Action<KeyboardInputEvent>;
                        Debug.Assert(keyHandler != null, nameof(keyHandler) + " != null");
                        keyHandler.Invoke(evt);
                    }

                    if (ran) {
                        RunWriteBindings(element);
                    }

                    return !evt.stopPropagation;
                });
            }

            else {
                UIElement element = m_FocusedElement;
                InputHandlerGroup evtHandlerGroup = m_FocusedElement.inputHandlers;
                if (evtHandlerGroup == null) {
                    return;
                }

                RunBindings(element);

                for (int i = 0; i < evtHandlerGroup.eventHandlers.size; i++) {
                    if (m_EventPropagator.shouldStopPropagation) break;
                    ref InputHandlerGroup.HandlerData handler = ref evtHandlerGroup.eventHandlers.array[i];
                    if (!ShouldRun(handler, keyInputEvent)) {
                        continue;
                    }

                    Action<KeyboardInputEvent> keyHandler = evtHandlerGroup.eventHandlers[i].handlerFn as Action<KeyboardInputEvent>;
                    Debug.Assert(keyHandler != null, nameof(keyHandler) + " != null");
                    keyHandler.Invoke(keyInputEvent);
                }

                RunWriteBindings(element);
            }
        }

        protected bool ShouldRun(in InputHandlerGroup.HandlerData handlerData, in KeyboardInputEvent evt) {
            if (evt.eventType != handlerData.eventType) return false;

            if (handlerData.requireFocus && !evt.isFocused) return false;

            if (handlerData.character != '\0' && (handlerData.character != evt.character)) return false;

            if (evt.keyCode != handlerData.keyCode && handlerData.keyCode != KeyCodeUtil.AnyKey) {
                return false;
            }

            // if all required modifiers are present these should be equal
            return (handlerData.modifiers & evt.modifiers) == handlerData.modifiers;
        }

        private void RunMouseEvents(List<UIElement> elements, InputEventType eventType) {
            if (elements.Count == 0) return;

            m_EventPropagator.Reset(mouseState);

            m_EventPropagator.origin = elements[0];
            for (int i = 0; i < elements.Count; i++) {
                UIElement element = elements[i];
                if (element.isDestroyed || element.isDisabled) {
                    continue;
                }

                if (element.inputHandlers == null || (element.inputHandlers.handledEvents & eventType) == 0) {
                    continue;
                }

                LightList<InputHandlerGroup.HandlerData> handlers = element.inputHandlers.eventHandlers;

                for (int j = 0; j < handlers.size; j++) {
                    InputHandlerGroup.HandlerData handlerData = handlers.array[j];

                    if (handlerData.eventType != eventType) {
                        continue;
                    }

                    if (handlerData.eventPhase != EventPhase.Bubble) {
                        m_MouseEventCaptureList.Add(ValueTuple.Create(handlerData.handlerFn, element));
                        continue;
                    }

                    if ((handlerData.modifiers & m_KeyboardState.modifiersThisFrame) == handlerData.modifiers) {
                        Action<MouseInputEvent> handler = handlerData.handlerFn as Action<MouseInputEvent>;
                        Debug.Assert(handler != null, nameof(handler) + " != null");
                        handler.Invoke(new MouseInputEvent(m_EventPropagator, eventType, m_KeyboardState.modifiersThisFrame, element == m_FocusedElement, element));
                    }

                    if (m_EventPropagator.shouldStopPropagation) {
                        break;
                    }
                }

                if (m_EventPropagator.shouldStopPropagation) {
                    m_MouseEventCaptureList.Clear();
                    return;
                }
            }

            for (int i = 0; i < m_MouseEventCaptureList.Count; i++) {
                Action<MouseInputEvent> handler = (Action<MouseInputEvent>) m_MouseEventCaptureList[i].Item1;
                UIElement element = m_MouseEventCaptureList[i].Item2;

                handler.Invoke(new MouseInputEvent(m_EventPropagator, eventType, m_KeyboardState.modifiersThisFrame, element == m_FocusedElement, element));

                if (m_EventPropagator.shouldStopPropagation) {
                    m_MouseEventCaptureList.Clear();
                    return;
                }
            }

            m_MouseEventCaptureList.Clear();
        }

        private void ProcessMouseEvents() {
            RunMouseEvents(m_ExitedElements, InputEventType.MouseExit);

            RunMouseEvents(m_EnteredElements, InputEventType.MouseEnter);
            if (mouseState.scrollDelta != Vector2.zero) {
                RunMouseEvents(m_ElementsThisFrame, InputEventType.MouseScroll);
            }

            if (mouseState.isLeftMouseDownThisFrame || mouseState.isRightMouseDownThisFrame || mouseState.isMiddleMouseDownThisFrame) {
                HandleBlur();

                if (m_ElementsThisFrame.Count > 0 && m_ElementsThisFrame[0].View.RequestFocus()) {
                    // todo let's see if we have to process the mouse event again
                }

                RunMouseEvents(m_ElementsThisFrame, InputEventType.MouseDown);
            }
            else if (mouseState.isLeftMouseUpThisFrame || mouseState.isMiddleMouseUpThisFrame) {
                RunMouseEvents(m_ElementsThisFrame, InputEventType.MouseUp);
                if (mouseState.clickCount > 0) {
                    RunMouseEvents(m_ElementsThisFrame, InputEventType.MouseClick);
                }
            }
            else if (mouseState.isRightMouseUpThisFrame) {
                RunMouseEvents(m_ElementsThisFrame, InputEventType.MouseUp);
                if (!mouseState.isLeftMouseDown && !mouseState.isMiddleMouseDown) {
                    RunMouseEvents(m_ElementsThisFrame, InputEventType.MouseContext);
                }
            }
            else if (mouseState.isLeftMouseDown || mouseState.isRightMouseDown || mouseState.isMiddleMouseDown) {
                RunMouseEvents(m_ElementsThisFrame, InputEventType.MouseHeldDown);
            }

            RunMouseEvents(m_ElementsThisFrame,
                mouseState.DidMove ? InputEventType.MouseMove : InputEventType.MouseHover);
        }

        private void HandleBlur() {
            if (m_FocusedElement == null) {
                return;
            }

            if (m_ElementsThisFrame.Count == 0) {
                ReleaseFocus((IFocusable) m_FocusedElement);
                return;
            }

            UIElement ptr = m_ElementsThisFrame[0];
            while (ptr != null) {
                if (ptr == m_FocusedElement) {
                    return;
                }

                ptr = ptr.parent;
            }

            ReleaseFocus((IFocusable) m_FocusedElement);
        }

    }

    public struct KeyboardEventHandlerInvocation {

        public KeyboardInputEvent evt { get; set; }

    }

    public class ElementComparer : IComparer<UIElement> {

        public ElementTable<ElementTraversalInfo> traversalTable;

        public int Compare(UIElement a, UIElement b) {
            ref ElementTraversalInfo x = ref traversalTable[a.id];
            ref ElementTraversalInfo y = ref traversalTable[b.id];

            if (x.zIndex != y.zIndex) {
                return y.zIndex - x.zIndex;
            }

            return y.ftbIndex - x.ftbIndex;
        }

    }

}