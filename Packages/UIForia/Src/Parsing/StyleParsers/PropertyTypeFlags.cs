using System;

namespace UIForia.Style {

    [Flags]
    public enum PropertyTypeFlags : byte {

        None = 0,
        Inherited = 1 << 0,
        Animated = 1 << 1,
        BuiltIn = 1 << 2,
        RequireDestruction = 1 << 3,
        Removed = 1 << 4

    }

}