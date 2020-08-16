﻿using System.Runtime.InteropServices;
using UnityEngine;

namespace UIForia.Graphics {

    [AssertSize(80)]
    [StructLayout(LayoutKind.Explicit)]
    public struct UIForiaMaterialInfo {

        [FieldOffset(0)] public TextMaterialInfo textMaterial;
        [FieldOffset(0)] public ElementMaterialInfo elementElementMaterial;
        [FieldOffset(0)] public ShadowMaterialInfo shadowMaterialInfo;

    }

    ///<summary>
    /// must be aligned on 16 byte boundaries for shader performance
    /// Could step this up to 64 if I needed to, might have to with masking Pie values for elements
    /// </summary>
    [AssertSize(80)]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TextMaterialInfo {

        public Color32 faceColor;
        public Color32 outlineColor;
        public Color32 glowColor;
        public Color32 underlayColor;

        public ushort faceDilate;
        public ushort underlayDilate;

        public float underlayX;
        public float underlayY;

        public byte glowPower;
        public byte glowInner;
        public byte glowOuter;
        public byte underlaySoftness;

        public ushort glowOffset;
        public byte outlineWidth;
        public byte outlineSoftness;

        private fixed byte padding[28];
        private uint unused0;
        private uint unused1;
        private uint unused2;
        private uint unused3;

    }

    [AssertSize(64)]
    [StructLayout(LayoutKind.Sequential)]
    public struct ElementMaterialInfoGPU {

        uint backgroundColor;
        uint backgroundTint;
        uint outlineColor;
        uint outlineTint; // not used atm 

        uint radius;
        uint bevelTop;
        uint bevelBottom;
        uint fillOpenAndRotation;
        float fillRadius;
        float fillOffsetX;
        float fillRadiusY;

        uint bMode_oMode_unused;

        // maybe move to float buffer

        float outlineWidth;
        uint unused0;
        uint unused1;
        uint unused2;

    }

    [AssertSize(80)]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ShadowMaterialInfo {

        public fixed byte padding[80];

    }

    ///<summary>
    /// must be aligned on 16 byte boundaries for shader performance
    /// </summary>
    [AssertSize(80)]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ElementMaterialInfo {

        public Color32 backgroundColor;
        public Color32 backgroundTint;
        public Color32 outlineColor;
        public Color32 outlineTint; // might not need or want this, assumes we are using an outline texture which I dont currently have implemented

        public Color32 borderColorTop;
        public Color32 borderColorRight;
        public Color32 borderColorBottom;
        public Color32 borderColorLeft;

        // would also want a texture transform for outline which I dont have atm
        // float4 hdrIntensities?
        public byte radius0;
        public byte radius1;
        public byte radius2;
        public byte radius3;

        public ushort bevelTL;
        public ushort bevelTR;
        public ushort bevelBR;
        public ushort bevelBL;

        public ushort fillOpenAmount;
        public ushort fillRotation;

        public float fillRadius;
        public float fillOffsetX;
        public float fillOffsetY;

        public ColorMode bodyColorMode;
        public ColorMode outlineColorMode;
        public byte fillDirection;
        public byte fillInvert;

        public float outlineWidth;
        public uint uvTransformId; // dont need this to be an int

        public ushort uvRotation;
        public ushort opacity;

        public uint borderIndex;

        // by putting these here we also free up texCoords in the actual vertices
        // could either encode some of the data there or re-purpose those
        // not sure I love these here, consider a seperate buffer
        // maybe a generic float4 buffer will suffice
        // public half uvOffsetX;
        // public half uvOffsetY;
        // public half uvScaleX;
        // public half uvScaleY;
        //
        // public half uvTileX;
        // public half uvTileY;
        // public half uvRotation;

    }

}