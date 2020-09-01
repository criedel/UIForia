﻿namespace UIForia {

    public struct ElementTraversalInfo {

        public int depth;
        public int ftbIndex;
        public int btfIndex;
        public int zIndex;
        // public ushort templateOriginId;
        // public ushort templateId;

        public bool IsDescendentOf(in ElementTraversalInfo info) {
            return ftbIndex > info.ftbIndex && btfIndex > info.btfIndex;
        }

//        public bool IsTemplateDescendentOf(in ElementTraversalInfo info) {
//            return templateOriginId == info.templateId && ftbIndex > info.ftbIndex && btfIndex > info.btfIndex;
//        }

        public bool IsAncestorOf(in ElementTraversalInfo info) {
            return ftbIndex < info.ftbIndex && btfIndex < info.btfIndex;
        }

        public bool IsParentOf(in ElementTraversalInfo info) {
            return depth == info.depth + 1 && ftbIndex < info.ftbIndex && btfIndex < info.btfIndex;
        }

        public bool IsChildOf(in ElementTraversalInfo info) {
            return info.depth == depth - 1 && ftbIndex < info.ftbIndex && btfIndex < info.btfIndex;
        }

        public bool IsTemplateChildOf(in ElementTraversalInfo info) {
            return info.depth == info.depth - 1 && ftbIndex < info.ftbIndex && btfIndex < info.btfIndex;
        }

        public bool IsLaterInHierarchy(in ElementTraversalInfo info) {
            return ftbIndex > info.ftbIndex;
        }

    }

}