//ccnode的锚点默认是左下角,0,0
using Cocos2D;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace liwq
{
    #region enums

    public enum HorizontalAlignment { HorizontalAlignmentStretch, HorizontalAlignmentLeft, HorizontalAlignmentCenter, HorizontalAlignmentRight };
    public enum VerticalAlignment { VerticalAlignmentStretch, VerticalAlignmentTop, VerticalAlignmentCenter, VerticalAlignmentBottom };
    public enum Stretch { StretchNone, StretchFill, StretchUniform, StretchUniformToFill };
    public enum AlignmentX { AlignmentXLeft, AlignmentXCenter, AlignmentXRight };
    public enum AlignmentY { AlignmentYTop, AlignmentYCenter, AlignmentYBottom };
    public enum Orientation { OrientationHorizontal, OrientationVertical };
    public enum Visibility { VisibilityVisible, VisibilityHidden, VisibilityCollapsed };
    public enum ClickMode { Release, Press, Hover }
    public struct Thickness
    {
        private float _left;
        private float _top;
        private float _right;
        private float _bottom;
        public float Left { get { return this._left; } }
        public float Top { get { return this._top; } }
        public float Right { get { return this._right; } }
        public float Bottom { get { return this._bottom; } }
        public Thickness(float uniformLength)
        {
            this._left = uniformLength;
            this._top = uniformLength;
            this._right = uniformLength;
            this._bottom = uniformLength;
        }
        public Thickness(float left, float top, float right, float bottom)
        {
            this._left = left;
            this._top = top;
            this._right = right;
            this._bottom = bottom;
        }
    }

    #endregion //enums

    #region Colors

    public class Colors
    {
        public static CCColor4B AliceBlue { get { return new CCColor4B(0xF0, 0xF8, 0xFF, 0xFF); } }
        public static CCColor4B AntiqueWhite { get { return new CCColor4B(0xFA, 0xEB, 0xD7, 0xFF); } }
        public static CCColor4B Aqua { get { return new CCColor4B(0x00, 0xFF, 0xFF, 0xFF); } }
        public static CCColor4B Aquamarine { get { return new CCColor4B(0x7F, 0xFF, 0xD4, 0xFF); } }
        public static CCColor4B Azure { get { return new CCColor4B(0xF0, 0xFF, 0xFF, 0xFF); } }
        public static CCColor4B Beige { get { return new CCColor4B(0xF5, 0xF5, 0xDC, 0xFF); } }
        public static CCColor4B Bisque { get { return new CCColor4B(0xFF, 0xE4, 0xC4, 0xFF); } }
        public static CCColor4B Black { get { return new CCColor4B(0x00, 0x00, 0x00, 0xFF); } }
        public static CCColor4B BlanchedAlmond { get { return new CCColor4B(0xFF, 0xEB, 0xCD, 0xFF); } }
        public static CCColor4B Blue { get { return new CCColor4B(0x00, 0x00, 0xFF, 0xFF); } }
        public static CCColor4B BlueViolet { get { return new CCColor4B(0x8A, 0x2B, 0xE2, 0xFF); } }
        public static CCColor4B Brown { get { return new CCColor4B(0xA5, 0x2A, 0x2A, 0xFF); } }
        public static CCColor4B BurlyWood { get { return new CCColor4B(0xDE, 0xB8, 0x87, 0xFF); } }
        public static CCColor4B CadetBlue { get { return new CCColor4B(0x5F, 0x9E, 0xA0, 0xFF); } }
        public static CCColor4B Chartreuse { get { return new CCColor4B(0x7F, 0xFF, 0x00, 0xFF); } }
        public static CCColor4B Chocolate { get { return new CCColor4B(0xD2, 0x69, 0x1E, 0xFF); } }
        public static CCColor4B Coral { get { return new CCColor4B(0xFF, 0x7F, 0x50, 0xFF); } }
        public static CCColor4B CornflowerBlue { get { return new CCColor4B(0x64, 0x95, 0xED, 0xFF); } }
        public static CCColor4B Cornsilk { get { return new CCColor4B(0xFF, 0xF8, 0xDC, 0xFF); } }
        public static CCColor4B Crimson { get { return new CCColor4B(0xDC, 0x14, 0x3C, 0xFF); } }
        public static CCColor4B Cyan { get { return new CCColor4B(0x00, 0xFF, 0xFF, 0xFF); } }
        public static CCColor4B DarkBlue { get { return new CCColor4B(0x00, 0x00, 0x8B, 0xFF); } }
        public static CCColor4B DarkCyan { get { return new CCColor4B(0x00, 0x8B, 0x8B, 0xFF); } }
        public static CCColor4B DarkGoldenrod { get { return new CCColor4B(0xB8, 0x86, 0x0B, 0xFF); } }
        public static CCColor4B DarkGray { get { return new CCColor4B(0xA9, 0xA9, 0xA9, 0xFF); } }
        public static CCColor4B DarkGreen { get { return new CCColor4B(0x00, 0x64, 0x00, 0xFF); } }
        public static CCColor4B DarkKhaki { get { return new CCColor4B(0xBD, 0xB7, 0x6B, 0xFF); } }
        public static CCColor4B DarkMagenta { get { return new CCColor4B(0x8B, 0x00, 0x8B, 0xFF); } }
        public static CCColor4B DarkOliveGreen { get { return new CCColor4B(0x55, 0x6B, 0x2F, 0xFF); } }
        public static CCColor4B DarkOrange { get { return new CCColor4B(0xFF, 0x8C, 0x00, 0xFF); } }
        public static CCColor4B DarkOrchid { get { return new CCColor4B(0x99, 0x32, 0xCC, 0xFF); } }
        public static CCColor4B DarkRed { get { return new CCColor4B(0x8B, 0x00, 0x00, 0xFF); } }
        public static CCColor4B DarkSalmon { get { return new CCColor4B(0xE9, 0x96, 0x7A, 0xFF); } }
        public static CCColor4B DarkSeaGreen { get { return new CCColor4B(0x8F, 0xBC, 0x8F, 0xFF); } }
        public static CCColor4B DarkSlateBlue { get { return new CCColor4B(0x48, 0x3D, 0x8B, 0xFF); } }
        public static CCColor4B DarkSlateGray { get { return new CCColor4B(0x2F, 0x4F, 0x4F, 0xFF); } }
        public static CCColor4B DarkTurquoise { get { return new CCColor4B(0x00, 0xCE, 0xD1, 0xFF); } }
        public static CCColor4B DarkViolet { get { return new CCColor4B(0x94, 0x00, 0xD3, 0xFF); } }
        public static CCColor4B DeepPink { get { return new CCColor4B(0xFF, 0x14, 0x93, 0xFF); } }
        public static CCColor4B DeepSkyBlue { get { return new CCColor4B(0x00, 0xBF, 0xFF, 0xFF); } }
        public static CCColor4B DimGray { get { return new CCColor4B(0x69, 0x69, 0x69, 0xFF); } }
        public static CCColor4B DodgerBlue { get { return new CCColor4B(0x1E, 0x90, 0xFF, 0xFF); } }
        public static CCColor4B Firebrick { get { return new CCColor4B(0xB2, 0x22, 0x22, 0xFF); } }
        public static CCColor4B FloralWhite { get { return new CCColor4B(0xFF, 0xFA, 0xF0, 0xFF); } }
        public static CCColor4B ForestGreen { get { return new CCColor4B(0x22, 0x8B, 0x22, 0xFF); } }
        public static CCColor4B Fuchsia { get { return new CCColor4B(0xFF, 0x00, 0xFF, 0xFF); } }
        public static CCColor4B Gainsboro { get { return new CCColor4B(0xDC, 0xDC, 0xDC, 0xFF); } }
        public static CCColor4B GhostWhite { get { return new CCColor4B(0xF8, 0xF8, 0xFF, 0xFF); } }
        public static CCColor4B Gold { get { return new CCColor4B(0xFF, 0xD7, 0x00, 0xFF); } }
        public static CCColor4B Goldenrod { get { return new CCColor4B(0xDA, 0xA5, 0x20, 0xFF); } }
        public static CCColor4B Gray { get { return new CCColor4B(0x80, 0x80, 0x80, 0xFF); } }
        public static CCColor4B Green { get { return new CCColor4B(0x00, 0x80, 0x00, 0xFF); } }
        public static CCColor4B GreenYellow { get { return new CCColor4B(0xAD, 0xFF, 0x2F, 0xFF); } }
        public static CCColor4B Honeydew { get { return new CCColor4B(0xF0, 0xFF, 0xF0, 0xFF); } }
        public static CCColor4B HotPink { get { return new CCColor4B(0xFF, 0x69, 0xB4, 0xFF); } }
        public static CCColor4B IndianRed { get { return new CCColor4B(0xCD, 0x5C, 0x5C, 0xFF); } }
        public static CCColor4B Indigo { get { return new CCColor4B(0x4B, 0x00, 0x82, 0xFF); } }
        public static CCColor4B Ivory { get { return new CCColor4B(0xFF, 0xFF, 0xF0, 0xFF); } }
        public static CCColor4B Khaki { get { return new CCColor4B(0xF0, 0xE6, 0x8C, 0xFF); } }
        public static CCColor4B Lavender { get { return new CCColor4B(0xE6, 0xE6, 0xFA, 0xFF); } }
        public static CCColor4B LavenderBlush { get { return new CCColor4B(0xFF, 0xF0, 0xF5, 0xFF); } }
        public static CCColor4B LawnGreen { get { return new CCColor4B(0x7C, 0xFC, 0x00, 0xFF); } }
        public static CCColor4B LemonChiffon { get { return new CCColor4B(0xFF, 0xFA, 0xCD, 0xFF); } }
        public static CCColor4B LightBlue { get { return new CCColor4B(0xAD, 0xD8, 0xE6, 0xFF); } }
        public static CCColor4B LightCoral { get { return new CCColor4B(0xF0, 0x80, 0x80, 0xFF); } }
        public static CCColor4B LightCyan { get { return new CCColor4B(0xE0, 0xFF, 0xFF, 0xFF); } }
        public static CCColor4B LightGoldenrodYellow { get { return new CCColor4B(0xFA, 0xFA, 0xD2, 0xFF); } }
        public static CCColor4B LightGray { get { return new CCColor4B(0xD3, 0xD3, 0xD3, 0xFF); } }
        public static CCColor4B LightGreen { get { return new CCColor4B(0x90, 0xEE, 0x90, 0xFF); } }
        public static CCColor4B LightPink { get { return new CCColor4B(0xFF, 0xB6, 0xC1, 0xFF); } }
        public static CCColor4B LightSalmon { get { return new CCColor4B(0xFF, 0xA0, 0x7A, 0xFF); } }
        public static CCColor4B LightSeaGreen { get { return new CCColor4B(0x20, 0xB2, 0xAA, 0xFF); } }
        public static CCColor4B LightSkyBlue { get { return new CCColor4B(0x87, 0xCE, 0xFA, 0xFF); } }
        public static CCColor4B LightSlateGray { get { return new CCColor4B(0x77, 0x88, 0x99, 0xFF); } }
        public static CCColor4B LightSteelBlue { get { return new CCColor4B(0xB0, 0xC4, 0xDE, 0xFF); } }
        public static CCColor4B LightYellow { get { return new CCColor4B(0xFF, 0xFF, 0xE0, 0xFF); } }
        public static CCColor4B Lime { get { return new CCColor4B(0x00, 0xFF, 0x00, 0xFF); } }
        public static CCColor4B LimeGreen { get { return new CCColor4B(0x32, 0xCD, 0x32, 0xFF); } }
        public static CCColor4B Linen { get { return new CCColor4B(0xFA, 0xF0, 0xE6, 0xFF); } }
        public static CCColor4B Magenta { get { return new CCColor4B(0xFF, 0x00, 0xFF, 0xFF); } }
        public static CCColor4B Maroon { get { return new CCColor4B(0x80, 0x00, 0x00, 0xFF); } }
        public static CCColor4B MediumAquamarine { get { return new CCColor4B(0x66, 0xCD, 0xAA, 0xFF); } }
        public static CCColor4B MediumBlue { get { return new CCColor4B(0x00, 0x00, 0xCD, 0xFF); } }
        public static CCColor4B MediumOrchid { get { return new CCColor4B(0xBA, 0x55, 0xD3, 0xFF); } }
        public static CCColor4B MediumPurple { get { return new CCColor4B(0x93, 0x70, 0xDB, 0xFF); } }
        public static CCColor4B MediumSeaGreen { get { return new CCColor4B(0x3C, 0xB3, 0x71, 0xFF); } }
        public static CCColor4B MediumSlateBlue { get { return new CCColor4B(0x7B, 0x68, 0xEE, 0xFF); } }
        public static CCColor4B MediumSpringGreen { get { return new CCColor4B(0x00, 0xFA, 0x9A, 0xFF); } }
        public static CCColor4B MediumTurquoise { get { return new CCColor4B(0x48, 0xD1, 0xCC, 0xFF); } }
        public static CCColor4B MediumVioletRed { get { return new CCColor4B(0xC7, 0x15, 0x85, 0xFF); } }
        public static CCColor4B MidnightBlue { get { return new CCColor4B(0x19, 0x19, 0x70, 0xFF); } }
        public static CCColor4B MintCream { get { return new CCColor4B(0xF5, 0xFF, 0xFA, 0xFF); } }
        public static CCColor4B MistyRose { get { return new CCColor4B(0xFF, 0xE4, 0xE1, 0xFF); } }
        public static CCColor4B Moccasin { get { return new CCColor4B(0xFF, 0xE4, 0xB5, 0xFF); } }
        public static CCColor4B NavajoWhite { get { return new CCColor4B(0xFF, 0xDE, 0xAD, 0xFF); } }
        public static CCColor4B Navy { get { return new CCColor4B(0x00, 0x00, 0x80, 0xFF); } }
        public static CCColor4B OldLace { get { return new CCColor4B(0xFD, 0xF5, 0xE6, 0xFF); } }
        public static CCColor4B Olive { get { return new CCColor4B(0x80, 0x80, 0x00, 0xFF); } }
        public static CCColor4B OliveDrab { get { return new CCColor4B(0x6B, 0x8E, 0x23, 0xFF); } }
        public static CCColor4B Orange { get { return new CCColor4B(0xFF, 0xA5, 0x00, 0xFF); } }
        public static CCColor4B OrangeRed { get { return new CCColor4B(0xFF, 0x45, 0x00, 0xFF); } }
        public static CCColor4B Orchid { get { return new CCColor4B(0xDA, 0x70, 0xD6, 0xFF); } }
        public static CCColor4B PaleGoldenrod { get { return new CCColor4B(0xEE, 0xE8, 0xAA, 0xFF); } }
        public static CCColor4B PaleGreen { get { return new CCColor4B(0x98, 0xFB, 0x98, 0xFF); } }
        public static CCColor4B PaleTurquoise { get { return new CCColor4B(0xAF, 0xEE, 0xEE, 0xFF); } }
        public static CCColor4B PaleVioletRed { get { return new CCColor4B(0xDB, 0x70, 0x93, 0xFF); } }
        public static CCColor4B PapayaWhip { get { return new CCColor4B(0xFF, 0xEF, 0xD5, 0xFF); } }
        public static CCColor4B PeachPuff { get { return new CCColor4B(0xFF, 0xDA, 0xB9, 0xFF); } }
        public static CCColor4B Peru { get { return new CCColor4B(0xCD, 0x85, 0x3F, 0xFF); } }
        public static CCColor4B Pink { get { return new CCColor4B(0xFF, 0xC0, 0xCB, 0xFF); } }
        public static CCColor4B Plum { get { return new CCColor4B(0xDD, 0xA0, 0xDD, 0xFF); } }
        public static CCColor4B PowderBlue { get { return new CCColor4B(0xB0, 0xE0, 0xE6, 0xFF); } }
        public static CCColor4B Purple { get { return new CCColor4B(0x80, 0x00, 0x80, 0xFF); } }
        public static CCColor4B Red { get { return new CCColor4B(0xFF, 0x00, 0x00, 0xFF); } }
        public static CCColor4B RosyBrown { get { return new CCColor4B(0xBC, 0x8F, 0x8F, 0xFF); } }
        public static CCColor4B RoyalBlue { get { return new CCColor4B(0x41, 0x69, 0xE1, 0xFF); } }
        public static CCColor4B SaddleBrown { get { return new CCColor4B(0x8B, 0x45, 0x13, 0xFF); } }
        public static CCColor4B Salmon { get { return new CCColor4B(0xFA, 0x80, 0x72, 0xFF); } }
        public static CCColor4B SandyBrown { get { return new CCColor4B(0xF4, 0xA4, 0x60, 0xFF); } }
        public static CCColor4B SeaGreen { get { return new CCColor4B(0x2E, 0x8B, 0x57, 0xFF); } }
        public static CCColor4B SeaShell { get { return new CCColor4B(0xFF, 0xF5, 0xEE, 0xFF); } }
        public static CCColor4B Sienna { get { return new CCColor4B(0xA0, 0x52, 0x2D, 0xFF); } }
        public static CCColor4B Silver { get { return new CCColor4B(0xC0, 0xC0, 0xC0, 0xFF); } }
        public static CCColor4B SkyBlue { get { return new CCColor4B(0x87, 0xCE, 0xEB, 0xFF); } }
        public static CCColor4B SlateBlue { get { return new CCColor4B(0x6A, 0x5A, 0xCD, 0xFF); } }
        public static CCColor4B SlateGray { get { return new CCColor4B(0x70, 0x80, 0x90, 0xFF); } }
        public static CCColor4B Snow { get { return new CCColor4B(0xFF, 0xFA, 0xFA, 0xFF); } }
        public static CCColor4B SpringGreen { get { return new CCColor4B(0x00, 0xFF, 0x7F, 0xFF); } }
        public static CCColor4B SteelBlue { get { return new CCColor4B(0x46, 0x82, 0xB4, 0xFF); } }
        public static CCColor4B Tan { get { return new CCColor4B(0xD2, 0xB4, 0x8C, 0xFF); } }
        public static CCColor4B Teal { get { return new CCColor4B(0x00, 0x80, 0x80, 0xFF); } }
        public static CCColor4B Thistle { get { return new CCColor4B(0xD8, 0xBF, 0xD8, 0xFF); } }
        public static CCColor4B Tomato { get { return new CCColor4B(0xFF, 0x63, 0x47, 0xFF); } }
        public static CCColor4B Transparent { get { return new CCColor4B(0xFF, 0xFF, 0xFF, 0x00); } }
        public static CCColor4B Turquoise { get { return new CCColor4B(0x40, 0xE0, 0xD0, 0xFF); } }
        public static CCColor4B Violet { get { return new CCColor4B(0xEE, 0x82, 0xEE, 0xFF); } }
        public static CCColor4B Wheat { get { return new CCColor4B(0xF5, 0xDE, 0xB3, 0xFF); } }
        public static CCColor4B White { get { return new CCColor4B(0xFF, 0xFF, 0xFF, 0xFF); } }
        public static CCColor4B WhiteSmoke { get { return new CCColor4B(0xF5, 0xF5, 0xF5, 0xFF); } }
        public static CCColor4B Yellow { get { return new CCColor4B(0xFF, 0xFF, 0x00, 0xFF); } }
        public static CCColor4B YellowGreen { get { return new CCColor4B(0x9A, 0xCD, 0x32, 0xFF); } }
        public static CCColor4B Ramdom { get { return new CCColor4B((byte)CCRandom.Next(), (byte)CCRandom.Next(), (byte)CCRandom.Next(), 0xFF); } }

        public static Dictionary<string, CCColor4B> _ColorDictionary = new Dictionary<string, CCColor4B>();
        public static void _AddColor(uint u32, string colorName)
        {
            _ColorDictionary.Add(colorName, new CCColor4B((byte)(u32 >> 16), (byte)(u32 >> 8), (byte)(u32), (byte)(u32 >> 24)));
        }
        static Colors()
        {
            _AddColor(0xFFF0F8FF, "AliceBlue");
            _AddColor(0xFFFAEBD7, "AntiqueWhite");
            _AddColor(0xFF00FFFF, "Aqua");
            _AddColor(0xFF7FFFD4, "Aquamarine");
            _AddColor(0xFFF0FFFF, "Azure");
            _AddColor(0xFFF5F5DC, "Beige");
            _AddColor(0xFFFFE4C4, "Bisque");
            _AddColor(0xFF000000, "Black");
            _AddColor(0xFFFFEBCD, "BlanchedAlmond");
            _AddColor(0xFF0000FF, "Blue");
            _AddColor(0xFF8A2BE2, "BlueViolet");
            _AddColor(0xFFA52A2A, "Brown");
            _AddColor(0xFFDEB887, "BurlyWood");
            _AddColor(0xFF5F9EA0, "CadetBlue");
            _AddColor(0xFF7FFF00, "Chartreuse");
            _AddColor(0xFFD2691E, "Chocolate");
            _AddColor(0xFFFF7F50, "Coral");
            _AddColor(0xFF6495ED, "CornflowerBlue");
            _AddColor(0xFFFFF8DC, "Cornsilk");
            _AddColor(0xFFDC143C, "Crimson");
            _AddColor(0xFF00FFFF, "Cyan");
            _AddColor(0xFF00008B, "DarkBlue");
            _AddColor(0xFF008B8B, "DarkCyan");
            _AddColor(0xFFB8860B, "DarkGoldenrod");
            _AddColor(0xFFA9A9A9, "DarkGray");
            _AddColor(0xFF006400, "DarkGreen");
            _AddColor(0xFFBDB76B, "DarkKhaki");
            _AddColor(0xFF8B008B, "DarkMagenta");
            _AddColor(0xFF556B2F, "DarkOliveGreen");
            _AddColor(0xFFFF8C00, "DarkOrange");
            _AddColor(0xFF9932CC, "DarkOrchid");
            _AddColor(0xFF8B0000, "DarkRed");
            _AddColor(0xFFE9967A, "DarkSalmon");
            _AddColor(0xFF8FBC8F, "DarkSeaGreen");
            _AddColor(0xFF483D8B, "DarkSlateBlue");
            _AddColor(0xFF2F4F4F, "DarkSlateGray");
            _AddColor(0xFF00CED1, "DarkTurquoise");
            _AddColor(0xFF9400D3, "DarkViolet");
            _AddColor(0xFFFF1493, "DeepPink");
            _AddColor(0xFF00BFFF, "DeepSkyBlue");
            _AddColor(0xFF696969, "DimGray");
            _AddColor(0xFF1E90FF, "DodgerBlue");
            _AddColor(0xFFB22222, "Firebrick");
            _AddColor(0xFFFFFAF0, "FloralWhite");
            _AddColor(0xFF228B22, "ForestGreen");
            _AddColor(0xFFFF00FF, "Fuchsia");
            _AddColor(0xFFDCDCDC, "Gainsboro");
            _AddColor(0xFFF8F8FF, "GhostWhite");
            _AddColor(0xFFFFD700, "Gold");
            _AddColor(0xFFDAA520, "Goldenrod");
            _AddColor(0xFF808080, "Gray");
            _AddColor(0xFF008000, "Green");
            _AddColor(0xFFADFF2F, "GreenYellow");
            _AddColor(0xFFF0FFF0, "Honeydew");
            _AddColor(0xFFFF69B4, "HotPink");
            _AddColor(0xFFCD5C5C, "IndianRed");
            _AddColor(0xFF4B0082, "Indigo");
            _AddColor(0xFFFFFFF0, "Ivory");
            _AddColor(0xFFF0E68C, "Khaki");
            _AddColor(0xFFE6E6FA, "Lavender");
            _AddColor(0xFFFFF0F5, "LavenderBlush");
            _AddColor(0xFF7CFC00, "LawnGreen");
            _AddColor(0xFFFFFACD, "LemonChiffon");
            _AddColor(0xFFADD8E6, "LightBlue");
            _AddColor(0xFFF08080, "LightCoral");
            _AddColor(0xFFE0FFFF, "LightCyan");
            _AddColor(0xFFFAFAD2, "LightGoldenrodYellow");
            _AddColor(0xFFD3D3D3, "LightGray");
            _AddColor(0xFF90EE90, "LightGreen");
            _AddColor(0xFFFFB6C1, "LightPink");
            _AddColor(0xFFFFA07A, "LightSalmon");
            _AddColor(0xFF20B2AA, "LightSeaGreen");
            _AddColor(0xFF87CEFA, "LightSkyBlue");
            _AddColor(0xFF778899, "LightSlateGray");
            _AddColor(0xFFB0C4DE, "LightSteelBlue");
            _AddColor(0xFFFFFFE0, "LightYellow");
            _AddColor(0xFF00FF00, "Lime");
            _AddColor(0xFF32CD32, "LimeGreen");
            _AddColor(0xFFFAF0E6, "Linen");
            _AddColor(0xFFFF00FF, "Magenta");
            _AddColor(0xFF800000, "Maroon");
            _AddColor(0xFF66CDAA, "MediumAquamarine");
            _AddColor(0xFF0000CD, "MediumBlue");
            _AddColor(0xFFBA55D3, "MediumOrchid");
            _AddColor(0xFF9370DB, "MediumPurple");
            _AddColor(0xFF3CB371, "MediumSeaGreen");
            _AddColor(0xFF7B68EE, "MediumSlateBlue");
            _AddColor(0xFF00FA9A, "MediumSpringGreen");
            _AddColor(0xFF48D1CC, "MediumTurquoise");
            _AddColor(0xFFC71585, "MediumVioletRed");
            _AddColor(0xFF191970, "MidnightBlue");
            _AddColor(0xFFF5FFFA, "MintCream");
            _AddColor(0xFFFFE4E1, "MistyRose");
            _AddColor(0xFFFFE4B5, "Moccasin");
            _AddColor(0xFFFFDEAD, "NavajoWhite");
            _AddColor(0xFF000080, "Navy");
            _AddColor(0xFFFDF5E6, "OldLace");
            _AddColor(0xFF808000, "Olive");
            _AddColor(0xFF6B8E23, "OliveDrab");
            _AddColor(0xFFFFA500, "Orange");
            _AddColor(0xFFFF4500, "OrangeRed");
            _AddColor(0xFFDA70D6, "Orchid");
            _AddColor(0xFFEEE8AA, "PaleGoldenrod");
            _AddColor(0xFF98FB98, "PaleGreen");
            _AddColor(0xFFAFEEEE, "PaleTurquoise");
            _AddColor(0xFFDB7093, "PaleVioletRed");
            _AddColor(0xFFFFEFD5, "PapayaWhip");
            _AddColor(0xFFFFDAB9, "PeachPuff");
            _AddColor(0xFFCD853F, "Peru");
            _AddColor(0xFFFFC0CB, "Pink");
            _AddColor(0xFFDDA0DD, "Plum");
            _AddColor(0xFFB0E0E6, "PowderBlue");
            _AddColor(0xFF800080, "Purple");
            _AddColor(0xFFFF0000, "Red");
            _AddColor(0xFFBC8F8F, "RosyBrown");
            _AddColor(0xFF4169E1, "RoyalBlue");
            _AddColor(0xFF8B4513, "SaddleBrown");
            _AddColor(0xFFFA8072, "Salmon");
            _AddColor(0xFFF4A460, "SandyBrown");
            _AddColor(0xFF2E8B57, "SeaGreen");
            _AddColor(0xFFFFF5EE, "SeaShell");
            _AddColor(0xFFA0522D, "Sienna");
            _AddColor(0xFFC0C0C0, "Silver");
            _AddColor(0xFF87CEEB, "SkyBlue");
            _AddColor(0xFF6A5ACD, "SlateBlue");
            _AddColor(0xFF708090, "SlateGray");
            _AddColor(0xFFFFFAFA, "Snow");
            _AddColor(0xFF00FF7F, "SpringGreen");
            _AddColor(0xFF4682B4, "SteelBlue");
            _AddColor(0xFFD2B48C, "Tan");
            _AddColor(0xFF008080, "Teal");
            _AddColor(0xFFD8BFD8, "Thistle");
            _AddColor(0xFFFF6347, "Tomato");
            _AddColor(0x00FFFFFF, "Transparent");
            _AddColor(0xFF40E0D0, "Turquoise");
            _AddColor(0xFFEE82EE, "Violet");
            _AddColor(0xFFF5DEB3, "Wheat");
            _AddColor(0xFFFFFFFF, "White");
            _AddColor(0xFFF5F5F5, "WhiteSmoke");
            _AddColor(0xFFFFFF00, "Yellow");
            _AddColor(0xFF9ACD32, "YellowGreen");
        }

        public static CCColor4B Parse(string colorName)
        {
            if (_ColorDictionary.ContainsKey(colorName) == true)
                return _ColorDictionary[colorName];
            if (colorName.StartsWith("#") == true)
            {
                colorName = colorName.Substring(1);
                uint u32 = Convert.ToUInt32(colorName, 16);
                return new CCColor4B((byte)(u32 >> 16), (byte)(u32 >> 8), (byte)(u32), (byte)(u32 >> 24));
            }
            return CCColor3B.White;
        }
    }

    #endregion //Colors

    #region IUIElement + IPanel + ITouch

    /// <summary>根据这些XAML对应属性，最终决定Position与ContentSize</summary>
    public interface IUIElement
    {
        float Left { get; set; }
        float Top { get; set; }
        float Right { get; set; }
        float Bottom { get; set; }

        float Width { get; set; }
        float Height { get; set; }

        Thickness Margin { get; set; }
        HorizontalAlignment HorizontalAlignment { get; set; }
        VerticalAlignment VerticalAlignment { get; set; }
    }

    /// <summary>
    /// 根据这些XAML属性，决定每个Child的Position与ContentSize
    /// </summary>
    public interface IPanel
    {
        void ArrangeChild();
        IUIElement FindName(string name);
        IUIElement this[string name] { get; }
    }

    public class Touch
    {
        public bool Handled { get; set; }
        public CCTouch CCTouch { get; private set; }
        public Touch(CCTouch t) { this.CCTouch = t; }
    }
    public delegate void TouchEventHandler(object sender, Touch e);
    public interface ITouch
    {
        event TouchEventHandler TouchDown;
        event TouchEventHandler TouchMove;
        event TouchEventHandler TouchUp;
        void OnTouchDown(Touch e);
        void OnTouchMove(Touch e);
        void OnTouchUp(Touch e);
    }

    #endregion //IUIElement + IPanel + ITouch

    #region Canvas

    public class Canvas : CCNode, IUIElement, IPanel
    {
        #region IUIElement

        protected float _left;
        public float Left
        {
            get { return this._left; }
            set { this._left = value; }
        }

        protected float _top;
        public float Top
        {
            get { return this._top; }
            set { this._top = value; }
        }

        protected float _right;
        public float Right
        {
            get { return this._right; }
            set { this._right = value; }
        }

        protected float _bottom;
        public float Bottom
        {
            get { return this._bottom; }
            set { this._bottom = value; }
        }

        protected float _width;
        public float Width
        {
            get { return this._width; }
            set { this._width = value; }
        }

        protected float _height;
        public float Height
        {
            get { return this._height; }
            set { this._height = value; }
        }

        protected Thickness _margin;
        public Thickness Margin
        {
            get { return this._margin; }
            set { this._margin = value; }
        }

        protected HorizontalAlignment _horizontalAlignment;
        public HorizontalAlignment HorizontalAlignment
        {
            get { return this._horizontalAlignment; }
            set { this._horizontalAlignment = value; }
        }

        public VerticalAlignment _verticalAlignment;
        public VerticalAlignment VerticalAlignment
        {
            get { return this._verticalAlignment; }
            set { this._verticalAlignment = value; }
        }

        #endregion //IUIElement

        #region IPanel

        public void ArrangeChild()
        {
            if (this.Children == null) return;
            var childs = this.Children;
            for (int i = 0; i < childs.Count; i++)
            {
                IUIElement ui = childs[i] as IUIElement;
                if (ui != null)
                {
                    CCNode node = ui as CCNode;
                    if (node != null)
                    {
                        node.ContentSize = new CCSize(ui.Width, ui.Height);
                        node.PositionX = (ui.Width * node.Scale / 2) + ui.Left + ui.Margin.Left;
                        node.PositionY = this.Height - (ui.Height * node.Scale / 2) - ui.Top - ui.Margin.Top;
                    }
                }
            }
        }

        public IUIElement FindName(string name)
        {
            if (this.Children == null) return null;
            var childs = this.Children;
            for (int i = 0; i < childs.Count; i++)
            {
                IUIElement ui = childs[i] as IUIElement;
                if (ui != null)
                {
                    if (childs[i].Name == name)
                        return ui;
                }
            }
            return null;
        }

        public IUIElement this[string name]
        {
            get { return this.FindName(name); }
        }

        #endregion //IPanel

        public Canvas()
        {
            this.AnchorPoint = new CCPoint(0.5f, 0.5f);
            this.Position = CCPoint.Zero;
            this.ContentSize = CCDirector.SharedDirector.WinSize;

            this.Left = this.PositionX;
            this.Right = this.PositionY;
            this.Width = this.ContentSize.Width;
            this.Height = this.ContentSize.Height;
        }

        private CCLayerColor _background;
        public CCColor4B Background
        {
            get
            {
                if (this._background == null) return new CCColor4B(0, 0, 0, 0);
                return new CCColor4B(this._background.Color.R, this._background.Color.G, this._background.Color.B, this._background.Opacity);
            }
            set
            {
                if (this._background == null)
                {
                    this._background = new CCLayerColor(value);
                    this._background.ContentSize = this.ContentSize;
                    this.AddChild(this._background, -1);
                }
                else
                {
                    this._background.Color = new CCColor3B(value.R, value.G, value.B);
                    this._background.Opacity = value.A;
                }
            }
        }

        /// <summary>随着canvas的contentsize改变而改变</summary>
        public override CCSize ContentSize
        {
            get { return base.ContentSize; }
            set
            {
                base.ContentSize = value;
                if (this._background != null)
                {
                    this._background.ContentSize = value;
                }
            }
        }
    }

    #endregion //Canvas

    #region Image

    public class Image : CCSprite, IUIElement, ITouch
    {
        #region IUIElement

        protected float _left;
        public float Left
        {
            get { return this._left; }
            set { this._left = value; }
        }

        protected float _top;
        public float Top
        {
            get { return this._top; }
            set { this._top = value; }
        }

        protected float _right;
        public float Right
        {
            get { return this._right; }
            set { this._right = value; }
        }

        protected float _bottom;
        public float Bottom
        {
            get { return this._bottom; }
            set { this._bottom = value; }
        }

        protected float _width;
        public float Width
        {
            get { return this._width; }
            set { this._width = value; }
        }

        protected float _height;
        public float Height
        {
            get { return this._height; }
            set { this._height = value; }
        }

        protected Thickness _margin;
        public Thickness Margin
        {
            get { return this._margin; }
            set { this._margin = value; }
        }

        protected HorizontalAlignment _horizontalAlignment;
        public HorizontalAlignment HorizontalAlignment
        {
            get { return this._horizontalAlignment; }
            set { this._horizontalAlignment = value; }
        }

        public VerticalAlignment _verticalAlignment;
        public VerticalAlignment VerticalAlignment
        {
            get { return this._verticalAlignment; }
            set { this._verticalAlignment = value; }
        }

        #endregion //IUIElement

        #region ITouch

        public event TouchEventHandler TouchDown;
        public event TouchEventHandler TouchMove;
        public event TouchEventHandler TouchUp;

        public void OnTouchDown(Touch e)
        {
            if (this.TouchDown != null)
                this.TouchDown(this, e);
        }
        public void OnTouchMove(Touch e)
        {
            if (this.TouchMove != null)
                this.TouchMove(this, e);
        }
        public void OnTouchUp(Touch e)
        {
            if (this.TouchUp != null)
                this.TouchUp(this, e);
        }

        #endregion //ITouch

        public Image(CCSpriteFrame spriteFrame)
            : base(spriteFrame)
        {
            this.Left = this.PositionX;
            this.Right = this.PositionY;
            this.Width = this.ContentSize.Width;
            this.Height = this.ContentSize.Height;
        }
    }

    #endregion //Image

    #region TextBlock

    public class TextBlock : XBMFont, IUIElement
    {
        #region IUIElement

        protected float _left;
        public float Left
        {
            get { return this._left; }
            set { this._left = value; }
        }

        protected float _top;
        public float Top
        {
            get { return this._top; }
            set { this._top = value; }
        }

        protected float _right;
        public float Right
        {
            get { return this._right; }
            set { this._right = value; }
        }

        protected float _bottom;
        public float Bottom
        {
            get { return this._bottom; }
            set { this._bottom = value; }
        }

        protected float _width;
        public float Width
        {
            get { return this._width; }
            set { this._width = value; }
        }

        protected float _height;
        public float Height
        {
            get { return this._height; }
            set { this._height = value; }
        }

        protected Thickness _margin;
        public Thickness Margin
        {
            get { return this._margin; }
            set { this._margin = value; }
        }

        //new protected HorizontalAlignment _horizontalAlignment;
        new public HorizontalAlignment HorizontalAlignment
        {
            get
            {
                switch (base.HorizontalAlignment)
                {
                    case CCTextAlignment.Left: return HorizontalAlignment.HorizontalAlignmentLeft;
                    case CCTextAlignment.Right: return HorizontalAlignment.HorizontalAlignmentRight;
                    case CCTextAlignment.Center: return HorizontalAlignment.HorizontalAlignmentCenter;
                }
                return HorizontalAlignment.HorizontalAlignmentStretch;
            }
            set
            {
                switch (value)
                {
                    case HorizontalAlignment.HorizontalAlignmentLeft: base.HorizontalAlignment = CCTextAlignment.Left; break;
                    case HorizontalAlignment.HorizontalAlignmentRight: base.HorizontalAlignment = CCTextAlignment.Right; break;
                    case HorizontalAlignment.HorizontalAlignmentCenter: base.HorizontalAlignment = CCTextAlignment.Center; break;
                }
            }
        }

        //new protected VerticalAlignment _verticalAlignment;
        new public VerticalAlignment VerticalAlignment
        {
            get
            {
                switch (base.VerticalAlignment)
                {
                    case CCVerticalTextAlignment.Top: return VerticalAlignment.VerticalAlignmentTop;
                    case CCVerticalTextAlignment.Bottom: return VerticalAlignment.VerticalAlignmentBottom;
                    case CCVerticalTextAlignment.Center: return VerticalAlignment.VerticalAlignmentCenter;
                }
                return liwq.VerticalAlignment.VerticalAlignmentStretch;
            }
            set
            {
                switch (value)
                {
                    case VerticalAlignment.VerticalAlignmentTop: base.VerticalAlignment = CCVerticalTextAlignment.Top; break;
                    case VerticalAlignment.VerticalAlignmentBottom: base.VerticalAlignment = CCVerticalTextAlignment.Bottom; break;
                    case VerticalAlignment.VerticalAlignmentCenter: base.VerticalAlignment = CCVerticalTextAlignment.Center; break;
                }
            }
        }

        #endregion //IUIElement

        public TextBlock(string fontFile, string text = "", float fontSize = 0)
            : base(text, fontFile)
        {
            if (fontSize == 0)
                fontSize = base._fontConfig.CommonHeight;

            if (fontSize != base._fontConfig.CommonHeight)
                this.Scale = fontSize / base._fontConfig.CommonHeight;

            this.Left = this.PositionX;
            this.Right = this.PositionY;
            this.Width = this.ContentSize.Width;
            this.Height = this.ContentSize.Height;
        }
    }

    #endregion //TextBlock

    #region Rectangle

    public class Rectangle : CCLayerColor, IUIElement, ITouch
    {
        #region IUIElement

        protected float _left;
        public float Left
        {
            get { return this._left; }
            set { this._left = value; }
        }

        protected float _top;
        public float Top
        {
            get { return this._top; }
            set { this._top = value; }
        }

        protected float _right;
        public float Right
        {
            get { return this._right; }
            set { this._right = value; }
        }

        protected float _bottom;
        public float Bottom
        {
            get { return this._bottom; }
            set { this._bottom = value; }
        }

        protected float _width;
        public float Width
        {
            get { return this._width; }
            set { this._width = value; }
        }

        protected float _height;
        public float Height
        {
            get { return this._height; }
            set { this._height = value; }
        }

        protected Thickness _margin;
        public Thickness Margin
        {
            get { return this._margin; }
            set { this._margin = value; }
        }

        protected HorizontalAlignment _horizontalAlignment;
        public HorizontalAlignment HorizontalAlignment
        {
            get { return this._horizontalAlignment; }
            set { this._horizontalAlignment = value; }
        }

        public VerticalAlignment _verticalAlignment;
        public VerticalAlignment VerticalAlignment
        {
            get { return this._verticalAlignment; }
            set { this._verticalAlignment = value; }
        }

        #endregion //IUIElement

        #region ITouch

        public event TouchEventHandler TouchDown;
        public event TouchEventHandler TouchMove;
        public event TouchEventHandler TouchUp;

        public void OnTouchDown(Touch e)
        {
            if (this.TouchDown != null)
                this.TouchDown(this, e);
        }
        public void OnTouchMove(Touch e)
        {
            if (this.TouchMove != null)
                this.TouchMove(this, e);
        }
        public void OnTouchUp(Touch e)
        {
            if (this.TouchUp != null)
                this.TouchUp(this, e);
        }

        #endregion //ITouch

        public Rectangle()
            : base()
        {
            this.Left = this.PositionX;
            this.Right = this.PositionY;
            this.Width = this.ContentSize.Width;
            this.Height = this.ContentSize.Height;
        }
        public Rectangle(CCColor4B color, float width, float height) : base(color, width, height)
        {
            this.Left = this.PositionX;
            this.Right = this.PositionY;
            this.Width = this.ContentSize.Width;
            this.Height = this.ContentSize.Height;
        }
        public Rectangle(CCColor4B color) : base(color)
        {
            this.Left = this.PositionX;
            this.Right = this.PositionY;
            this.Width = this.ContentSize.Width;
            this.Height = this.ContentSize.Height;
        }
    }

    #endregion //Rectangle

    public class ListBox
    {
        //锚点以元素的左下角为准(0,0)
        public static Canvas CreateListLayer(CCNode[] nodes, float marginLeft, float marginTop, float marginBottom, float marginRight)
        {
            float layerWidth = 0;
            float layerHeight = 0;
            foreach (CCNode node in nodes)
            {
                float nodeWidth = node.ContentSize.Width + marginLeft + marginRight;
                float nodeHeight = node.ContentSize.Height + marginTop + marginBottom;
                if (nodeWidth > layerWidth)
                    layerWidth = nodeWidth;
                layerHeight += nodeHeight;
            }
            Canvas canvas = new Canvas() { ContentSize = new CCSize(layerWidth, layerHeight) };

            //CCLayerColor color = new CCLayerColor(CCColor4B.Yellow, layerWidth, layerHeight);
            //canvas.AddChild(color);

            float posX = marginLeft;
            float posY = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                posY += (nodes[i].ContentSize.Height + marginTop);
                nodes[i].PositionX = posX;
                nodes[i].PositionY = layerHeight - posY;
                posY += marginBottom;
                canvas.AddChild(nodes[i]);
            }

            return canvas;
        }
    }

    public static class XElementExtensions
    {
        public static float SafeGetFloat(this XElement element, string attributeName)
        {
            var attribute = element.Attribute(attributeName);
            if (attribute != null)
            {
                float result = 0;
                float.TryParse(attribute.Value, out result);
                return result;
            }
            return 0;
        }
        public static string SafeGetText(this XElement element, string attributeName)
        {
            var attribute = element.Attribute(attributeName);
            if (attribute != null)
                return attribute.Value;
            return "";
        }
        public static string SafeGetName(this XElement element)
        {
            return SafeGetText(element, "{http://schemas.microsoft.com/winfx/2006/xaml}Name");
        }

        //public static XElement SafeGetElement(this XElement element, string elementName)
        //{
        //    var value = element.Element("{http://schemas.microsoft.com/winfx/2006/xaml/presentation}" + elementName);
        //    return value;
        //}
        //public static string SafeAttribute(this XElement xml, string attributeName)
        //{
        //    string ns = xml.Name.NamespaceName.ToString();
        //    string path = ns + "/" + attributeName;
        //    var value = xml.Attribute(path);
        //    if (value != null) return value.Value;
        //    return "";
        //}
    }

    public class XAML
    {
        public static Canvas LoadComponent(string xaml)
        {
            XElement x = XElement.Parse(xaml);
            XElement xRoot = x.Elements().FirstOrDefault();
            Canvas canvas = null;
            if (xRoot.Name.LocalName == "Canvas")
            {
                canvas = new Canvas() { Name = xRoot.SafeGetName(), Width = xRoot.SafeGetFloat("Width"), Height = xRoot.SafeGetFloat("Height") };
                canvas.ContentSize = new CCSize(canvas.Width, canvas.Height);

                var eList = xRoot.Elements().ToList();
                foreach (var e in eList)
                {
                    switch (e.Name.LocalName)
                    {
                        case "Image":
                            {
                                if (e.Attribute("Source") != null)
                                {
                                    string filename = e.Attribute("Source").Value;
                                    Image image = new Image(Factory.CreateSpriteFrame(filename)) { Name = e.SafeGetName() };
                                    image.Width = e.SafeGetFloat("Width");
                                    image.Height = e.SafeGetFloat("Height");
                                    image.Left = e.SafeGetFloat("Canvas.Left");
                                    image.Top = e.SafeGetFloat("Canvas.Top");
                                    image.Visible = (e.SafeGetText("Visibility") != "Collapsed");
                                    if (e.Attribute("Opacity") != null)
                                        image.Opacity = (byte)(255 * e.SafeGetFloat("Opacity"));
                                    canvas.AddChild(image);
                                }
                            }
                            break;
                        case "TextBlock":
                            {
                                string fontFamily = e.SafeGetText("FontFamily");
                                if (fontFamily == "")
                                {
                                    //fontFamily = "Content/font.fnt";
                                    continue;
                                }
                                TextBlock textblock = new TextBlock(fontFamily, e.SafeGetText("Text"), e.SafeGetFloat("FontSize")) { Name = e.SafeGetName() };
                                textblock.Left = e.SafeGetFloat("Canvas.Left");
                                textblock.Top = e.SafeGetFloat("Canvas.Top");
                                textblock.Visible = (e.SafeGetText("Visibility") != "Collapsed");
                                if (e.Attribute("Opacity") != null)
                                    textblock.Opacity = (byte)(255 * e.SafeGetFloat("Opacity"));
                                if (e.Attribute("Foreground") != null)
                                {
                                    CCColor4B color = Colors.Parse(e.SafeGetText("Foreground"));
                                    textblock.Color = new CCColor3B(color.R, color.G, color.B);
                                }
                                canvas.AddChild(textblock);
                            }
                            break;
                    }
                }
            }
            canvas.ArrangeChild();
            return canvas;
        }

        public static void RouteTouchUp(CCNode root, Touch touch)
        {
            CCPoint touchLocation = touch.CCTouch.Location;
            if (root.ChildrenCount > 0)
            {
                for (int i = root.ChildrenCount - 1; i >= 0; i--)
                {
                    RouteTouchUp(root.Children[i], touch);  //递归
                    if (touch.Handled == true) return;
                    ITouch itouch = root.Children[i] as ITouch;
                    if (itouch != null)
                    {
                        CCNode node = root.Children[i] as CCNode;
                        if (node.Parent.Visible == true && node.Visible == true)
                        {
                            CCPoint local = node.ConvertToNodeSpace(touchLocation);
                            CCRect r = new CCRect(
                                node.PositionX - node.ContentSize.Width * node.AnchorPoint.X,
                                node.PositionY - node.ContentSize.Height * node.AnchorPoint.Y,
                                node.ContentSize.Width,
                                node.ContentSize.Height
                                );
                            r.Origin = CCPoint.Zero;
                            if (r.ContainsPoint(local))
                                itouch.OnTouchDown(touch);
                        }
                    }
                }
            }
        }
        public static void RouteTouchMove(CCNode root, Touch touch)
        {
            CCPoint touchLocation = touch.CCTouch.Location;
            if (root.ChildrenCount > 0)
            {
                for (int i = root.ChildrenCount - 1; i >= 0; i--)
                {
                    RouteTouchMove(root.Children[i], touch);  //递归
                    if (touch.Handled == true) return;
                    ITouch itouch = root.Children[i] as ITouch;
                    if (itouch != null)
                    {
                        CCNode node = root.Children[i] as CCNode;
                        if (node.Parent.Visible == true && node.Visible == true)
                        {
                            CCPoint local = node.ConvertToNodeSpace(touchLocation);
                            CCRect r = new CCRect(
                                node.PositionX - node.ContentSize.Width * node.AnchorPoint.X,
                                node.PositionY - node.ContentSize.Height * node.AnchorPoint.Y,
                                node.ContentSize.Width,
                                node.ContentSize.Height
                                );
                            r.Origin = CCPoint.Zero;
                            if (r.ContainsPoint(local))
                                itouch.OnTouchMove(touch);
                        }
                    }
                }
            }
        }

        public static void RouteTouchDown(CCNode root, Touch touch)
        {
            CCPoint touchLocation = touch.CCTouch.Location;
            if (root.ChildrenCount > 0)
            {
                for (int i = root.ChildrenCount - 1; i >= 0; i--)
                {
                    RouteTouchDown(root.Children[i], touch);  //递归
                    if (touch.Handled == true) return;
                    ITouch itouch = root.Children[i] as ITouch;
                    if (itouch != null)
                    {
                        CCNode node = root.Children[i] as CCNode;
                        if (node.Parent.Visible == true && node.Visible == true)
                        {
                            CCPoint local = node.ConvertToNodeSpace(touchLocation);
                            CCRect r = new CCRect(
                                node.PositionX - node.ContentSize.Width * node.AnchorPoint.X,
                                node.PositionY - node.ContentSize.Height * node.AnchorPoint.Y,
                                node.ContentSize.Width,
                                node.ContentSize.Height
                                );
                            r.Origin = CCPoint.Zero;
                            if (r.ContainsPoint(local))
                                itouch.OnTouchDown(touch);
                        }
                    }
                }
            }
        }

    }
}
