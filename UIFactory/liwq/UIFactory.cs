//Graphics2D的clear有bug，假定格式为bgra，而我们使用的是rgba

using Cocos2D;
using MatterHackers.Agg;
using MatterHackers.Agg.Font;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.Transform;
using MatterHackers.Agg.VertexSource;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace liwq
{
    #region MiniLanguage
    public static class MiniLanguage
    {
        public static IVertexSource CreatePathStorage(string data)
        {
            PathStorage path = new PathStorage();
            bool hasCurves = false;

            //todo   依次输入多个同一类型的命令时，可以省略重复的命令项；例如，L 100,200 300,400 等同于 L 100,200 L 300,400。
            StringBuilder buffer = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == '-' && i > 0 && data[i - 1] != ',' && data[i - 1] != ' ' && char.IsLetter(data[i - 1]) == false)
                {
                    buffer.Append(',');
                }
                if (data[i] != 'F' && (char.IsLetter(data[i])))
                {
                    buffer.Append(',');
                    buffer.Append(data[i]);
                    if (i < data.Length - 1 && data[i + 1] != ' ')
                        buffer.Append(',');
                }
                else
                {
                    buffer.Append(data[i]);
                }
            }
            string[] commands = buffer.ToString().Split(new char[] { ' ', ',' });

            MatterHackers.VectorMath.Vector2 polyStart = new MatterHackers.VectorMath.Vector2(0, 0);
            MatterHackers.VectorMath.Vector2 lastXY = new MatterHackers.VectorMath.Vector2(0, 0);
            MatterHackers.VectorMath.Vector2 curXY = new MatterHackers.VectorMath.Vector2();
            for (int i = 0; i < commands.Length; i++)
            {
                switch (commands[i])
                {
                    // F0 指定 EvenOdd 填充规则。F1 指定 Nonzero 填充规则。
                    // 如果省略此命令，则子路径使用默认行为，即 EvenOdd。 如果指定此命令，则必须将其置于最前面。 
                    case "F1": break;
                    case " ": break;

                    case "m":
                    case "M":
                        {
                            curXY.x = double.Parse(commands[i + 1]);
                            curXY.y = double.Parse(commands[i + 2]);
                            if (commands[i] == "m")
                            {
                                curXY += lastXY;
                            }
                            path.MoveTo(curXY.x, curXY.y);
                            polyStart = curXY;
                            i += 2;
                        }
                        break;
                    case "l":
                    case "L":
                        {
                            curXY.x = double.Parse(commands[i + 1]);
                            curXY.y = double.Parse(commands[i + 2]);
                            if (commands[i] == "l")
                            {
                                curXY += lastXY;
                            }
                            path.LineTo(curXY.x, curXY.y);
                        }
                        break;
                    case "h":
                    case "H":
                        {
                            curXY.y = lastXY.y;
                            curXY.x = double.Parse(commands[i + 1]);
                            if (commands[i] == "h")
                            {
                                curXY.x += lastXY.x;
                            }
                            path.HorizontalLineTo(curXY.x);
                            i += 1;
                        }
                        break;

                    case "v":
                    case "V":
                        {
                            curXY.x = lastXY.x;
                            curXY.y = double.Parse(commands[i + 1]);
                            if (commands[i] == "v")
                            {
                                curXY.y += lastXY.y;
                            }
                            path.VerticalLineTo(curXY.y);
                            i += 1;
                        }
                        break;

                    //三次方贝塞尔曲线命令。通过使用两个指定的控制点（controlPoint1 和 controlPoint2）在当前点与指定的终点之间创建一条三次方贝塞尔曲线
                    case "c":
                    case "C":
                        {
                            MatterHackers.VectorMath.Vector2 controlPoint1;
                            MatterHackers.VectorMath.Vector2 controlPoint2;
                            controlPoint1.x = double.Parse(commands[i + 1]);
                            controlPoint1.y = double.Parse(commands[i + 2]);
                            controlPoint2.x = double.Parse(commands[i + 3]);
                            controlPoint2.y = double.Parse(commands[i + 4]);
                            curXY.x = double.Parse(commands[i + 5]);
                            curXY.y = double.Parse(commands[i + 6]);
                            if (commands[i] == "c")
                            {
                                controlPoint1 += lastXY;
                                controlPoint2 += lastXY;
                                curXY += lastXY;
                            }
                            path.curve4(controlPoint1.x, controlPoint1.y, controlPoint2.x, controlPoint2.y, curXY.x, curXY.y);
                            i += 6;
                            hasCurves = true;
                        }
                        break;

                    //二次贝塞尔曲线命令。通过使用指定的控制点 (controlPoint) 在当前点与指定的终点之间创建一条二次贝塞尔曲线。
                    case "q":
                    case "Q":
                        {
                            MatterHackers.VectorMath.Vector2 controlPoint;
                            controlPoint.x = double.Parse(commands[i + 1]);
                            controlPoint.y = double.Parse(commands[i + 2]);
                            curXY.x = double.Parse(commands[i + 3]);
                            curXY.y = double.Parse(commands[i + 4]);
                            if (commands[i] == "q")
                            {
                                controlPoint += lastXY;
                                curXY += lastXY;
                            }
                            path.curve3(controlPoint.x, controlPoint.y, curXY.x, curXY.y);
                            i += 4;
                            hasCurves = true;
                        }
                        break;

                    //在当前点与指定的终点之间创建一条三次方贝塞尔曲线。 
                    //第一个控制点假定为前一个命令的第二个控制点相对于当前点的反射。 
                    //如果前一个命令不存在，或者前一个命令不是三次方贝塞尔曲线命令或平滑的三次方贝塞尔曲线命令，则假定第一个控制点就是当前点。 
                    //第二个控制点，即曲线终端的控制点，由 controlPoint2 指定。 
                    case "s":
                    case "S":
                        {
                            MatterHackers.VectorMath.Vector2 controlPoint2;
                            controlPoint2.x = double.Parse(commands[i + 1]);
                            controlPoint2.y = double.Parse(commands[i + 2]);
                            curXY.x = double.Parse(commands[i + 3]);
                            curXY.y = double.Parse(commands[i + 4]);
                            if (commands[i] == "s")
                            {
                                controlPoint2 += lastXY;
                                curXY += lastXY;
                            }
                            path.curve4(controlPoint2.x, controlPoint2.y, curXY.x, curXY.y);
                            i += 4;
                            hasCurves = true;
                        }
                        break;

                    //二次贝塞尔曲线命令。在当前点与指定的终点之间创建一条二次贝塞尔曲线。 
                    //控制点假定为前一个命令的控制点相对于当前点的反射。 
                    //如果前一个命令不存在，或者前一个命令不是二次贝塞尔曲线命令或平滑的二次贝塞尔曲线命令，则此控制点就是当前点。 
                    case "t":
                    case "T":
                        {
                            curXY.x = double.Parse(commands[i + 1]);
                            curXY.y = double.Parse(commands[i + 2]);
                            if (commands[i] == "t")
                            {
                                curXY += lastXY;
                            }
                            path.curve3(curXY.x, curXY.y);
                            i += 2;
                            hasCurves = true;
                        }
                        break;

                    case "z":
                    case "Z":
                        {
                            curXY = lastXY; // value not used this is to remove an error.
                            path.ClosePolygon();
                            // svg fonts are stored cw and agg expects its shapes to be ccw.  cw shapes are holes.
                            // We stored the position of the start of this polygon, no we flip it as we colse it.
                            //path.invert_polygon(0);
                        }
                        break;

                    case "\r":
                        {
                            curXY = lastXY; // value not used this is to remove an error.
                        }
                        break;
                }
                lastXY = curXY;
            }
            if (hasCurves == true)
                return new FlattenCurves(path);
            return path;
        }
    }
    #endregion //MiniLanguage

#if false
    #region Colors
    public static class Colors
    {
        public static RGBA_Bytes AliceBlue { get { return new RGBA_Bytes(0xF0, 0xF8, 0xFF, 0xFF); } }
        public static RGBA_Bytes AntiqueWhite { get { return new RGBA_Bytes(0xFA, 0xEB, 0xD7, 0xFF); } }
        public static RGBA_Bytes Aqua { get { return new RGBA_Bytes(0x00, 0xFF, 0xFF, 0xFF); } }
        public static RGBA_Bytes Aquamarine { get { return new RGBA_Bytes(0x7F, 0xFF, 0xD4, 0xFF); } }
        public static RGBA_Bytes Azure { get { return new RGBA_Bytes(0xF0, 0xFF, 0xFF, 0xFF); } }
        public static RGBA_Bytes Beige { get { return new RGBA_Bytes(0xF5, 0xF5, 0xDC, 0xFF); } }
        public static RGBA_Bytes Bisque { get { return new RGBA_Bytes(0xFF, 0xE4, 0xC4, 0xFF); } }
        public static RGBA_Bytes Black { get { return new RGBA_Bytes(0x00, 0x00, 0x00, 0xFF); } }
        public static RGBA_Bytes BlanchedAlmond { get { return new RGBA_Bytes(0xFF, 0xEB, 0xCD, 0xFF); } }
        public static RGBA_Bytes Blue { get { return new RGBA_Bytes(0x00, 0x00, 0xFF, 0xFF); } }
        public static RGBA_Bytes BlueViolet { get { return new RGBA_Bytes(0x8A, 0x2B, 0xE2, 0xFF); } }
        public static RGBA_Bytes Brown { get { return new RGBA_Bytes(0xA5, 0x2A, 0x2A, 0xFF); } }
        public static RGBA_Bytes BurlyWood { get { return new RGBA_Bytes(0xDE, 0xB8, 0x87, 0xFF); } }
        public static RGBA_Bytes CadetBlue { get { return new RGBA_Bytes(0x5F, 0x9E, 0xA0, 0xFF); } }
        public static RGBA_Bytes Chartreuse { get { return new RGBA_Bytes(0x7F, 0xFF, 0x00, 0xFF); } }
        public static RGBA_Bytes Chocolate { get { return new RGBA_Bytes(0xD2, 0x69, 0x1E, 0xFF); } }
        public static RGBA_Bytes Coral { get { return new RGBA_Bytes(0xFF, 0x7F, 0x50, 0xFF); } }
        public static RGBA_Bytes CornflowerBlue { get { return new RGBA_Bytes(0x64, 0x95, 0xED, 0xFF); } }
        public static RGBA_Bytes Cornsilk { get { return new RGBA_Bytes(0xFF, 0xF8, 0xDC, 0xFF); } }
        public static RGBA_Bytes Crimson { get { return new RGBA_Bytes(0xDC, 0x14, 0x3C, 0xFF); } }
        public static RGBA_Bytes Cyan { get { return new RGBA_Bytes(0x00, 0xFF, 0xFF, 0xFF); } }
        public static RGBA_Bytes DarkBlue { get { return new RGBA_Bytes(0x00, 0x00, 0x8B, 0xFF); } }
        public static RGBA_Bytes DarkCyan { get { return new RGBA_Bytes(0x00, 0x8B, 0x8B, 0xFF); } }
        public static RGBA_Bytes DarkGoldenrod { get { return new RGBA_Bytes(0xB8, 0x86, 0x0B, 0xFF); } }
        public static RGBA_Bytes DarkGray { get { return new RGBA_Bytes(0xA9, 0xA9, 0xA9, 0xFF); } }
        public static RGBA_Bytes DarkGreen { get { return new RGBA_Bytes(0x00, 0x64, 0x00, 0xFF); } }
        public static RGBA_Bytes DarkKhaki { get { return new RGBA_Bytes(0xBD, 0xB7, 0x6B, 0xFF); } }
        public static RGBA_Bytes DarkMagenta { get { return new RGBA_Bytes(0x8B, 0x00, 0x8B, 0xFF); } }
        public static RGBA_Bytes DarkOliveGreen { get { return new RGBA_Bytes(0x55, 0x6B, 0x2F, 0xFF); } }
        public static RGBA_Bytes DarkOrange { get { return new RGBA_Bytes(0xFF, 0x8C, 0x00, 0xFF); } }
        public static RGBA_Bytes DarkOrchid { get { return new RGBA_Bytes(0x99, 0x32, 0xCC, 0xFF); } }
        public static RGBA_Bytes DarkRed { get { return new RGBA_Bytes(0x8B, 0x00, 0x00, 0xFF); } }
        public static RGBA_Bytes DarkSalmon { get { return new RGBA_Bytes(0xE9, 0x96, 0x7A, 0xFF); } }
        public static RGBA_Bytes DarkSeaGreen { get { return new RGBA_Bytes(0x8F, 0xBC, 0x8F, 0xFF); } }
        public static RGBA_Bytes DarkSlateBlue { get { return new RGBA_Bytes(0x48, 0x3D, 0x8B, 0xFF); } }
        public static RGBA_Bytes DarkSlateGray { get { return new RGBA_Bytes(0x2F, 0x4F, 0x4F, 0xFF); } }
        public static RGBA_Bytes DarkTurquoise { get { return new RGBA_Bytes(0x00, 0xCE, 0xD1, 0xFF); } }
        public static RGBA_Bytes DarkViolet { get { return new RGBA_Bytes(0x94, 0x00, 0xD3, 0xFF); } }
        public static RGBA_Bytes DeepPink { get { return new RGBA_Bytes(0xFF, 0x14, 0x93, 0xFF); } }
        public static RGBA_Bytes DeepSkyBlue { get { return new RGBA_Bytes(0x00, 0xBF, 0xFF, 0xFF); } }
        public static RGBA_Bytes DimGray { get { return new RGBA_Bytes(0x69, 0x69, 0x69, 0xFF); } }
        public static RGBA_Bytes DodgerBlue { get { return new RGBA_Bytes(0x1E, 0x90, 0xFF, 0xFF); } }
        public static RGBA_Bytes Firebrick { get { return new RGBA_Bytes(0xB2, 0x22, 0x22, 0xFF); } }
        public static RGBA_Bytes FloralWhite { get { return new RGBA_Bytes(0xFF, 0xFA, 0xF0, 0xFF); } }
        public static RGBA_Bytes ForestGreen { get { return new RGBA_Bytes(0x22, 0x8B, 0x22, 0xFF); } }
        public static RGBA_Bytes Fuchsia { get { return new RGBA_Bytes(0xFF, 0x00, 0xFF, 0xFF); } }
        public static RGBA_Bytes Gainsboro { get { return new RGBA_Bytes(0xDC, 0xDC, 0xDC, 0xFF); } }
        public static RGBA_Bytes GhostWhite { get { return new RGBA_Bytes(0xF8, 0xF8, 0xFF, 0xFF); } }
        public static RGBA_Bytes Gold { get { return new RGBA_Bytes(0xFF, 0xD7, 0x00, 0xFF); } }
        public static RGBA_Bytes Goldenrod { get { return new RGBA_Bytes(0xDA, 0xA5, 0x20, 0xFF); } }
        public static RGBA_Bytes Gray { get { return new RGBA_Bytes(0x80, 0x80, 0x80, 0xFF); } }
        public static RGBA_Bytes Green { get { return new RGBA_Bytes(0x00, 0x80, 0x00, 0xFF); } }
        public static RGBA_Bytes GreenYellow { get { return new RGBA_Bytes(0xAD, 0xFF, 0x2F, 0xFF); } }
        public static RGBA_Bytes Honeydew { get { return new RGBA_Bytes(0xF0, 0xFF, 0xF0, 0xFF); } }
        public static RGBA_Bytes HotPink { get { return new RGBA_Bytes(0xFF, 0x69, 0xB4, 0xFF); } }
        public static RGBA_Bytes IndianRed { get { return new RGBA_Bytes(0xCD, 0x5C, 0x5C, 0xFF); } }
        public static RGBA_Bytes Indigo { get { return new RGBA_Bytes(0x4B, 0x00, 0x82, 0xFF); } }
        public static RGBA_Bytes Ivory { get { return new RGBA_Bytes(0xFF, 0xFF, 0xF0, 0xFF); } }
        public static RGBA_Bytes Khaki { get { return new RGBA_Bytes(0xF0, 0xE6, 0x8C, 0xFF); } }
        public static RGBA_Bytes Lavender { get { return new RGBA_Bytes(0xE6, 0xE6, 0xFA, 0xFF); } }
        public static RGBA_Bytes LavenderBlush { get { return new RGBA_Bytes(0xFF, 0xF0, 0xF5, 0xFF); } }
        public static RGBA_Bytes LawnGreen { get { return new RGBA_Bytes(0x7C, 0xFC, 0x00, 0xFF); } }
        public static RGBA_Bytes LemonChiffon { get { return new RGBA_Bytes(0xFF, 0xFA, 0xCD, 0xFF); } }
        public static RGBA_Bytes LightBlue { get { return new RGBA_Bytes(0xAD, 0xD8, 0xE6, 0xFF); } }
        public static RGBA_Bytes LightCoral { get { return new RGBA_Bytes(0xF0, 0x80, 0x80, 0xFF); } }
        public static RGBA_Bytes LightCyan { get { return new RGBA_Bytes(0xE0, 0xFF, 0xFF, 0xFF); } }
        public static RGBA_Bytes LightGoldenrodYellow { get { return new RGBA_Bytes(0xFA, 0xFA, 0xD2, 0xFF); } }
        public static RGBA_Bytes LightGray { get { return new RGBA_Bytes(0xD3, 0xD3, 0xD3, 0xFF); } }
        public static RGBA_Bytes LightGreen { get { return new RGBA_Bytes(0x90, 0xEE, 0x90, 0xFF); } }
        public static RGBA_Bytes LightPink { get { return new RGBA_Bytes(0xFF, 0xB6, 0xC1, 0xFF); } }
        public static RGBA_Bytes LightSalmon { get { return new RGBA_Bytes(0xFF, 0xA0, 0x7A, 0xFF); } }
        public static RGBA_Bytes LightSeaGreen { get { return new RGBA_Bytes(0x20, 0xB2, 0xAA, 0xFF); } }
        public static RGBA_Bytes LightSkyBlue { get { return new RGBA_Bytes(0x87, 0xCE, 0xFA, 0xFF); } }
        public static RGBA_Bytes LightSlateGray { get { return new RGBA_Bytes(0x77, 0x88, 0x99, 0xFF); } }
        public static RGBA_Bytes LightSteelBlue { get { return new RGBA_Bytes(0xB0, 0xC4, 0xDE, 0xFF); } }
        public static RGBA_Bytes LightYellow { get { return new RGBA_Bytes(0xFF, 0xFF, 0xE0, 0xFF); } }
        public static RGBA_Bytes Lime { get { return new RGBA_Bytes(0x00, 0xFF, 0x00, 0xFF); } }
        public static RGBA_Bytes LimeGreen { get { return new RGBA_Bytes(0x32, 0xCD, 0x32, 0xFF); } }
        public static RGBA_Bytes Linen { get { return new RGBA_Bytes(0xFA, 0xF0, 0xE6, 0xFF); } }
        public static RGBA_Bytes Magenta { get { return new RGBA_Bytes(0xFF, 0x00, 0xFF, 0xFF); } }
        public static RGBA_Bytes Maroon { get { return new RGBA_Bytes(0x80, 0x00, 0x00, 0xFF); } }
        public static RGBA_Bytes MediumAquamarine { get { return new RGBA_Bytes(0x66, 0xCD, 0xAA, 0xFF); } }
        public static RGBA_Bytes MediumBlue { get { return new RGBA_Bytes(0x00, 0x00, 0xCD, 0xFF); } }
        public static RGBA_Bytes MediumOrchid { get { return new RGBA_Bytes(0xBA, 0x55, 0xD3, 0xFF); } }
        public static RGBA_Bytes MediumPurple { get { return new RGBA_Bytes(0x93, 0x70, 0xDB, 0xFF); } }
        public static RGBA_Bytes MediumSeaGreen { get { return new RGBA_Bytes(0x3C, 0xB3, 0x71, 0xFF); } }
        public static RGBA_Bytes MediumSlateBlue { get { return new RGBA_Bytes(0x7B, 0x68, 0xEE, 0xFF); } }
        public static RGBA_Bytes MediumSpringGreen { get { return new RGBA_Bytes(0x00, 0xFA, 0x9A, 0xFF); } }
        public static RGBA_Bytes MediumTurquoise { get { return new RGBA_Bytes(0x48, 0xD1, 0xCC, 0xFF); } }
        public static RGBA_Bytes MediumVioletRed { get { return new RGBA_Bytes(0xC7, 0x15, 0x85, 0xFF); } }
        public static RGBA_Bytes MidnightBlue { get { return new RGBA_Bytes(0x19, 0x19, 0x70, 0xFF); } }
        public static RGBA_Bytes MintCream { get { return new RGBA_Bytes(0xF5, 0xFF, 0xFA, 0xFF); } }
        public static RGBA_Bytes MistyRose { get { return new RGBA_Bytes(0xFF, 0xE4, 0xE1, 0xFF); } }
        public static RGBA_Bytes Moccasin { get { return new RGBA_Bytes(0xFF, 0xE4, 0xB5, 0xFF); } }
        public static RGBA_Bytes NavajoWhite { get { return new RGBA_Bytes(0xFF, 0xDE, 0xAD, 0xFF); } }
        public static RGBA_Bytes Navy { get { return new RGBA_Bytes(0x00, 0x00, 0x80, 0xFF); } }
        public static RGBA_Bytes OldLace { get { return new RGBA_Bytes(0xFD, 0xF5, 0xE6, 0xFF); } }
        public static RGBA_Bytes Olive { get { return new RGBA_Bytes(0x80, 0x80, 0x00, 0xFF); } }
        public static RGBA_Bytes OliveDrab { get { return new RGBA_Bytes(0x6B, 0x8E, 0x23, 0xFF); } }
        public static RGBA_Bytes Orange { get { return new RGBA_Bytes(0xFF, 0xA5, 0x00, 0xFF); } }
        public static RGBA_Bytes OrangeRed { get { return new RGBA_Bytes(0xFF, 0x45, 0x00, 0xFF); } }
        public static RGBA_Bytes Orchid { get { return new RGBA_Bytes(0xDA, 0x70, 0xD6, 0xFF); } }
        public static RGBA_Bytes PaleGoldenrod { get { return new RGBA_Bytes(0xEE, 0xE8, 0xAA, 0xFF); } }
        public static RGBA_Bytes PaleGreen { get { return new RGBA_Bytes(0x98, 0xFB, 0x98, 0xFF); } }
        public static RGBA_Bytes PaleTurquoise { get { return new RGBA_Bytes(0xAF, 0xEE, 0xEE, 0xFF); } }
        public static RGBA_Bytes PaleVioletRed { get { return new RGBA_Bytes(0xDB, 0x70, 0x93, 0xFF); } }
        public static RGBA_Bytes PapayaWhip { get { return new RGBA_Bytes(0xFF, 0xEF, 0xD5, 0xFF); } }
        public static RGBA_Bytes PeachPuff { get { return new RGBA_Bytes(0xFF, 0xDA, 0xB9, 0xFF); } }
        public static RGBA_Bytes Peru { get { return new RGBA_Bytes(0xCD, 0x85, 0x3F, 0xFF); } }
        public static RGBA_Bytes Pink { get { return new RGBA_Bytes(0xFF, 0xC0, 0xCB, 0xFF); } }
        public static RGBA_Bytes Plum { get { return new RGBA_Bytes(0xDD, 0xA0, 0xDD, 0xFF); } }
        public static RGBA_Bytes PowderBlue { get { return new RGBA_Bytes(0xB0, 0xE0, 0xE6, 0xFF); } }
        public static RGBA_Bytes Purple { get { return new RGBA_Bytes(0x80, 0x00, 0x80, 0xFF); } }
        public static RGBA_Bytes Red { get { return new RGBA_Bytes(0xFF, 0x00, 0x00, 0xFF); } }
        public static RGBA_Bytes RosyBrown { get { return new RGBA_Bytes(0xBC, 0x8F, 0x8F, 0xFF); } }
        public static RGBA_Bytes RoyalBlue { get { return new RGBA_Bytes(0x41, 0x69, 0xE1, 0xFF); } }
        public static RGBA_Bytes SaddleBrown { get { return new RGBA_Bytes(0x8B, 0x45, 0x13, 0xFF); } }
        public static RGBA_Bytes Salmon { get { return new RGBA_Bytes(0xFA, 0x80, 0x72, 0xFF); } }
        public static RGBA_Bytes SandyBrown { get { return new RGBA_Bytes(0xF4, 0xA4, 0x60, 0xFF); } }
        public static RGBA_Bytes SeaGreen { get { return new RGBA_Bytes(0x2E, 0x8B, 0x57, 0xFF); } }
        public static RGBA_Bytes SeaShell { get { return new RGBA_Bytes(0xFF, 0xF5, 0xEE, 0xFF); } }
        public static RGBA_Bytes Sienna { get { return new RGBA_Bytes(0xA0, 0x52, 0x2D, 0xFF); } }
        public static RGBA_Bytes Silver { get { return new RGBA_Bytes(0xC0, 0xC0, 0xC0, 0xFF); } }
        public static RGBA_Bytes SkyBlue { get { return new RGBA_Bytes(0x87, 0xCE, 0xEB, 0xFF); } }
        public static RGBA_Bytes SlateBlue { get { return new RGBA_Bytes(0x6A, 0x5A, 0xCD, 0xFF); } }
        public static RGBA_Bytes SlateGray { get { return new RGBA_Bytes(0x70, 0x80, 0x90, 0xFF); } }
        public static RGBA_Bytes Snow { get { return new RGBA_Bytes(0xFF, 0xFA, 0xFA, 0xFF); } }
        public static RGBA_Bytes SpringGreen { get { return new RGBA_Bytes(0x00, 0xFF, 0x7F, 0xFF); } }
        public static RGBA_Bytes SteelBlue { get { return new RGBA_Bytes(0x46, 0x82, 0xB4, 0xFF); } }
        public static RGBA_Bytes Tan { get { return new RGBA_Bytes(0xD2, 0xB4, 0x8C, 0xFF); } }
        public static RGBA_Bytes Teal { get { return new RGBA_Bytes(0x00, 0x80, 0x80, 0xFF); } }
        public static RGBA_Bytes Thistle { get { return new RGBA_Bytes(0xD8, 0xBF, 0xD8, 0xFF); } }
        public static RGBA_Bytes Tomato { get { return new RGBA_Bytes(0xFF, 0x63, 0x47, 0xFF); } }
        public static RGBA_Bytes Transparent { get { return new RGBA_Bytes(0xFF, 0xFF, 0xFF, 0x00); } }
        public static RGBA_Bytes Turquoise { get { return new RGBA_Bytes(0x40, 0xE0, 0xD0, 0xFF); } }
        public static RGBA_Bytes Violet { get { return new RGBA_Bytes(0xEE, 0x82, 0xEE, 0xFF); } }
        public static RGBA_Bytes Wheat { get { return new RGBA_Bytes(0xF5, 0xDE, 0xB3, 0xFF); } }
        public static RGBA_Bytes White { get { return new RGBA_Bytes(0xFF, 0xFF, 0xFF, 0xFF); } }
        public static RGBA_Bytes WhiteSmoke { get { return new RGBA_Bytes(0xF5, 0xF5, 0xF5, 0xFF); } }
        public static RGBA_Bytes Yellow { get { return new RGBA_Bytes(0xFF, 0xFF, 0x00, 0xFF); } }
        public static RGBA_Bytes YellowGreen { get { return new RGBA_Bytes(0x9A, 0xCD, 0x32, 0xFF); } }
        public static RGBA_Bytes Ramdom { get { return new RGBA_Bytes((byte)CCRandom.Next(), (byte)CCRandom.Next(), (byte)CCRandom.Next(), 0xFF); } }
    }
    #endregion //Colors
#endif


    public class UIGraphic
    {
        public enum Stretch { StretchNone, StretchFill, StretchUniform, StretchUniformToFill };

        private ImageBuffer imageBuffer;
        private Graphics2D graphics2D;
        public static UIGraphic Begin(double width, double height)
        {
            UIGraphic g = new UIGraphic();
            g.imageBuffer = new ImageBuffer((int)width, (int)height, 32, new BlenderRGBA());
            g.graphics2D = g.imageBuffer.NewGraphics2D();
            return g;
        }
        public CCTexture2D EndTexture()
        {
            Texture2D xnaTexture = new Texture2D(CCApplication.SharedApplication.GraphicsDevice, this.imageBuffer.Width, this.imageBuffer.Height);
            xnaTexture.SetData<byte>(this.imageBuffer.GetBuffer());
            CCTexture2D ccTexture = new CCTexture2D();
            ccTexture.InitWithTexture(xnaTexture);
            return ccTexture;
        }
        public CCSprite EndSprite()
        {
            return new CCSprite(this.EndTexture());
        }
        private UIGraphic() { }

        public UIGraphic Clear(CCColor4B color)
        {
            this.graphics2D.Clear(new RGBA_Bytes(color.R, color.G, color.B, color.A));
            return this;
        }

        public UIGraphic AddRectangle(double x, double y, double width, double height, CCColor4B fill, CCColor4B stroke, double radiusX = 0, double radiusY = 0, double strokeThickness = 1)
        {
            IVertexSource path;
            if (stroke.A > 0) //有border
            {
                //border是以线的中间对齐，所以转换成int，如果是1个像素，有圆角变成1，没圆角变成0
                int halfThickness = (radiusX != 0 || radiusY != 0) ? (int)((strokeThickness + 1) / 2) : (int)(strokeThickness / 2);
                path = new RoundedRect(halfThickness, halfThickness, width - halfThickness, height - halfThickness, Math.Min(radiusX, radiusY));
            }
            else
            {
                path = new RoundedRect(0, 0, width, height, Math.Min(radiusX, radiusY));
            }
            if (x != 0 || y != 0)
                path = new VertexSourceApplyTransform(path, Affine.NewTranslation(x, y));
            if (fill.A > 0) this.graphics2D.Render(path, new RGBA_Bytes(fill.R, fill.G, fill.B, fill.A));
            if (stroke.A > 0) this.graphics2D.Render(new Stroke(path, strokeThickness), new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));
            return this;
        }

        public UIGraphic AddEllipse(double x, double y, double width, double height, CCColor4B fill, CCColor4B stroke, double strokeThickness = 1)
        {
            IVertexSource path;
            if (stroke.A > 0) //有border
            {
                //border是以线的中间对齐，所以转换成int，如果是1个像素变成1
                int halfThickness = (int)((strokeThickness + 1) / 2);
                path = new MatterHackers.Agg.VertexSource.Ellipse(width / 2, height / 2, width / 2 - halfThickness, height / 2 - halfThickness);
            }
            else
            {
                path = new MatterHackers.Agg.VertexSource.Ellipse(width / 2, height / 2, width / 2, height / 2);
            }

            if (x != 0 || y != 0)
                path = new VertexSourceApplyTransform(path, Affine.NewTranslation(x, y));
            if (fill.A > 0) this.graphics2D.Render(path, new RGBA_Bytes(fill.R, fill.G, fill.B, fill.A));
            if (stroke.A > 0) this.graphics2D.Render(new Stroke(path, strokeThickness), new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));
            return this;
        }

        public UIGraphic AddLine(float x1, float y1, float x2, float y2, CCColor4B stroke)
        {
            int width = (int)Math.Abs(x1 - x2);
            int height = (int)Math.Abs(y1 - y2);
            if (stroke.A > 0) this.graphics2D.Line(x1, y1, x2, y2, new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));
            return this;
        }

        public UIGraphic AddPaths(
            double x, double y,
            double width, double height,
            string[] paths,
            double contentX, double contentY,
            double contentWidth, double contentHeight, 
            CCColor4B fill, CCColor4B stroke,
            double strokeThickness = 1,
            Stretch stretch = Stretch.StretchFill
        )
        {
            if (width == 0) width = contentWidth;
            if (height == 0) height = contentHeight;

            double scalex = 0;
            double scaley = 0;
            //if (stretch == Stretch.StretchNone) { } else 
            if (stretch == Stretch.StretchFill)
            {
                if (width != contentWidth || height != contentHeight)
                {
                    scalex = width / contentWidth;
                    scaley = height / contentHeight;
                }
            }
            else if (stretch == Stretch.StretchUniformToFill)
            {
                scalex = scaley = Math.Min(width / contentWidth, height / contentHeight);
            }

            foreach (string path in paths)
            {
                IVertexSource vertexs = MiniLanguage.CreatePathStorage(path);
                if (x != 0 || y != 0 || contentX != 0 || contentY != 0)
                    vertexs = new VertexSourceApplyTransform(vertexs, Affine.NewTranslation(x - contentX, y - contentY));

                if (scalex != 1.0 || scaley != 1.0)
                    vertexs = new VertexSourceApplyTransform(vertexs, Affine.NewScaling(scalex, scaley));

                if (fill.A > 0) this.graphics2D.Render(vertexs, new RGBA_Bytes(fill.R, fill.G, fill.B, fill.A));
                if (stroke.A > 0) this.graphics2D.Render(new Stroke(vertexs, strokeThickness), new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));
            }

            return this;
        }
        public UIGraphic AddPath(
            double x, double y,
            double width, double height,
            string paths,
            double contentX, double contentY,
            double contentWidth, double contentHeight,
            CCColor4B fill, CCColor4B stroke,
            double strokeThickness = 1,
            Stretch stretch = Stretch.StretchFill
            )
        {
            return this.AddPaths(x, y, width, height, new string[1] { paths }, contentX, contentY, contentWidth, contentHeight, fill, stroke, strokeThickness, stretch);
        }

        public UIGraphic AddSvg(double x, double y, double width, double height, string svg)
        {
            XElement xroot = XElement.Parse(svg);
            double contentWidth = double.Parse(xroot.Attribute("width").Value.Replace("px", ""));
            double contentHeight = double.Parse(xroot.Attribute("height").Value.Replace("px", ""));
            double contentX = 0;
            double contentY = 0;

            CCColor4B fillColor = CCColor4B.White;
            double strokeWidth = 1.0;
            if (xroot.Attribute("fill") != null)
            {
                string value = xroot.Attribute("fill").Value;
                if (value.StartsWith("#") == true && value.Length == 7)
                {
                    byte r = byte.Parse(value.Substring(1, 2));
                    byte g = byte.Parse(value.Substring(3, 2));
                    byte b = byte.Parse(value.Substring(5, 2));
                    fillColor = new CCColor4B(r, g, b);
                }
            }
            if (xroot.Attribute("stroke-width") != null)
            {
                strokeWidth = double.Parse(xroot.Attribute("stroke-width").Value);
            }
            if (xroot.Attribute("viewBox") != null)
            {
                string[] values = xroot.Attribute("viewBox").Value.Split(new char[] { ',', ' ' });
                if (values.Length == 4)
                {
                    contentX = double.Parse(values[0]);
                    contentY = double.Parse(values[1]);
                }
            }

            CCColor4B brush = fillColor;

            XElement[] xpaths = xroot.Elements().Where(xe => xe.Name.LocalName == "path").ToArray();
            string[] pathDatas = new string[xpaths.Length];
            for (int i = 0; i < xpaths.Length; i++)
            {
                pathDatas[i] = xpaths[i].Attribute("d").Value;
            }

            if (width == 0 || height == 0 || (width == contentWidth && height == contentHeight))
            {
                return this.AddPaths(x, y, width, height, pathDatas, contentX, contentY, contentWidth, contentHeight, brush, brush, strokeWidth, Stretch.StretchFill);
            }
            return AddPaths(x, y, width, height, pathDatas, contentX, contentY, contentWidth, contentHeight, brush, brush, strokeWidth, Stretch.StretchFill);
        }

        public UIGraphic AddText(double x, double y, string text, CCColor4B fill, CCColor4B stroke, TypeFace font, double emSizeInPoints, bool underline = false, bool flatenCurves = true, double strokeThickness = 1)
        {
            TypeFacePrinter printer;
            if (font != null)
                printer = new TypeFacePrinter(text, new StyledTypeFace(font, emSizeInPoints, underline, flatenCurves));
            else
                printer = new TypeFacePrinter(text, emSizeInPoints);

            RectangleDouble rect = new RectangleDouble();
            bounding_rect.bounding_rect_single(printer, 0, ref rect);
            VertexSourceApplyTransform path = path = new VertexSourceApplyTransform(printer, Affine.NewTranslation(x - rect.Left, y - rect.Bottom));

            if (fill.A > 0) this.graphics2D.Render(path, new RGBA_Bytes(fill.R, fill.G, fill.B, fill.A));
            if (stroke.A > 0) this.graphics2D.Render(new Stroke(path, strokeThickness), new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));
            return this;
        }
    }

#if true
    public class UIFactory
    {
        #region texture from buffer
        public static Texture2D XnaTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(CCApplication.SharedApplication.GraphicsDevice, width, height);
            return texture;
        }
        public static Texture2D XnaTextureFromPng(Stream pngStream)
        {
            PngDecoder decoder = new PngDecoder();
            byte[] colors = decoder.Decode(pngStream);
            Texture2D xnaTexture = XnaTexture(decoder.Width, decoder.Height);
            xnaTexture.SetData<byte>(colors);
            return xnaTexture;
        }
        public static CCTexture2D CCTextureFromPng(Stream pngStream)
        {
            Texture2D xnaTexture = XnaTextureFromPng(pngStream);
            CCTexture2D ccTexture = new CCTexture2D();
            ccTexture.InitWithTexture(xnaTexture);
            return ccTexture;
        }
        public static CCSprite SpriteFromPng(Stream pngStream)
        {
            CCTexture2D ccTexture = CCTextureFromPng(pngStream);
            CCSprite sprite = new CCSprite(ccTexture);
            return sprite;
        }
        #endregion //texture from buffer

        //矩形
        public static CCSprite CreateRectangle(
            double width, double height,
            double radiusX, double radiusY,
            CCColor4B fill, CCColor4B stroke,
            double strokeThickness = 1
            )
        {
            ImageBuffer buffer = new ImageBuffer((int)width, (int)height, 32, new BlenderRGBA());
            Graphics2D g = buffer.NewGraphics2D();
            RoundedRect path;
            if (stroke.A > 0) //有border
            {
                //border是以线的中间对齐，所以转换成int，如果是1个像素，正好变成零
                int halfThickness = (int)(strokeThickness / 2);
                path = new RoundedRect(halfThickness, halfThickness, width - halfThickness, height - halfThickness, Math.Min(radiusX, radiusY));
            }
            else
            {
                path = new RoundedRect(0, 0, width, height, Math.Min(radiusX, radiusY));
            }
            if (fill.A > 0) g.Render(path, new RGBA_Bytes(fill.R, fill.G, fill.B, fill.A));
            if (stroke.A > 0) g.Render(new Stroke(path, strokeThickness), new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));

            Texture2D xnaTexture = XnaTexture((int)width, (int)height);
            xnaTexture.SetData<byte>(buffer.GetBuffer());
            CCTexture2D ccTexture = new CCTexture2D();
            ccTexture.InitWithTexture(xnaTexture);
            return new CCSprite(ccTexture);
        }

        //圆形
        public static CCSprite CreateEllipse(
            double width, double height,
            CCColor4B fill, CCColor4B stroke,
            double strokeThickness = 1
            )
        {
            ImageBuffer buffer = new ImageBuffer((int)width, (int)height, 32, new BlenderRGBA());
            Graphics2D g = buffer.NewGraphics2D();
            MatterHackers.Agg.VertexSource.Ellipse path;
            if (stroke.A > 0) //有border
            {
                //border是以线的中间对齐，所以转换成int，如果是1个像素，正好变成零
                int halfThickness = (int)(strokeThickness / 2);
                path = new MatterHackers.Agg.VertexSource.Ellipse(width / 2, height / 2, width / 2 - halfThickness, height / 2 - halfThickness);
            }
            else
            {
                path = new MatterHackers.Agg.VertexSource.Ellipse(width / 2, height / 2, width / 2, height / 2);
            }
            if (fill.A > 0) g.Render(path, new RGBA_Bytes(fill.R, fill.G, fill.B, fill.A));
            if (stroke.A > 0) g.Render(new Stroke(path, strokeThickness), new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));
            Texture2D xnaTexture = XnaTexture((int)width, (int)height);
            xnaTexture.SetData<byte>(buffer.GetBuffer());
            CCTexture2D ccTexture = new CCTexture2D();
            ccTexture.InitWithTexture(xnaTexture);
            return new CCSprite(ccTexture);
        }

        //线
        public static CCSprite CreateLine(
            float x1, float y1, float x2, float y2,
            CCColor4B stroke,
            double strokeThickness = 1
            )
        {
            int width = (int)Math.Abs(x1 - x2);
            int height = (int)Math.Abs(y1 - y2);
            return CreateLine(width, height, stroke, strokeThickness);
        }

        public static CCSprite CreateLine(
            int width, int height,
            CCColor4B stroke,
            double strokeThickness = 1
            )
        {
            ImageBuffer buffer = new ImageBuffer(width, height, 32, new BlenderRGBA());
            Graphics2D g = buffer.NewGraphics2D();
            if (stroke.A > 0)
            {
                //g.Line没有厚度
                PathStorage linesToDraw = new PathStorage();
                linesToDraw.remove_all();
                linesToDraw.MoveTo(0, 0);
                linesToDraw.LineTo(width, height);
                Stroke StrockedLineToDraw = new Stroke(linesToDraw, strokeThickness);
                g.Render(StrockedLineToDraw, new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));
            }
            Texture2D xnaTexture = XnaTexture((int)width, (int)height);
            xnaTexture.SetData<byte>(buffer.GetBuffer());
            CCTexture2D ccTexture = new CCTexture2D();
            ccTexture.InitWithTexture(xnaTexture);
            return new CCSprite(ccTexture);
        }

        //路径
        public static CCSprite CreatePath(
            double width, double height,
            string[] paths,
            double contentX, double contentY,
            double contentWidth, double contentHeight,
            CCColor4B fill, CCColor4B stroke,
            double strokeThickness = 1,
            UIGraphic.Stretch stretch = UIGraphic.Stretch.StretchNone
            )
        {
            if (width == 0) width = contentWidth;
            if (height == 0) height = contentHeight;

            ImageBuffer buffer = new ImageBuffer((int)width, (int)height, 32, new BlenderRGBA());
            Graphics2D g = buffer.NewGraphics2D();

            double scalex = 0;
            double scaley = 0;
            //if (stretch == Stretch.StretchNone) { } else 
            if (stretch == UIGraphic.Stretch.StretchFill)
            {
                if (width != contentWidth || height != contentHeight)
                {
                    scalex = width / contentWidth;
                    scaley = height / contentHeight;
                }
            }
            else if (stretch == UIGraphic.Stretch.StretchUniformToFill)
            {
                scalex = scaley = Math.Min(width / contentWidth, height / contentHeight);
            }

            foreach (string path in paths)
            {
                IVertexSource vertexs = MiniLanguage.CreatePathStorage(path);
                if (contentX != 0 || contentY != 0)
                    vertexs = new VertexSourceApplyTransform(vertexs, Affine.NewTranslation(-contentX, -contentY));

                if (scalex != 0 || scaley != 0)
                    vertexs = new VertexSourceApplyTransform(vertexs, Affine.NewScaling(scalex, scaley));

                if (fill.A > 0) g.Render(vertexs, new RGBA_Bytes(fill.R, fill.G, fill.B, fill.A));
                if (stroke.A > 0) g.Render(new Stroke(vertexs, strokeThickness), new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));
            }

            Texture2D xnaTexture = XnaTexture((int)width, (int)height);
            xnaTexture.SetData<byte>(buffer.GetBuffer());
            CCTexture2D ccTexture = new CCTexture2D();
            ccTexture.InitWithTexture(xnaTexture);
            return new CCSprite(ccTexture);
        }

        public static CCSprite CreatePathFromSVG(string svg, double width, double height, CCColor4B defaultColor)
        {
            XElement xroot = XElement.Parse(svg);
            double contentWidth = double.Parse(xroot.Attribute("width").Value.Replace("px", ""));
            double contentHeight = double.Parse(xroot.Attribute("height").Value.Replace("px", ""));
            double contentX = 0;
            double contentY = 0;

            CCColor4B fillColor = defaultColor;
            double strokeWidth = 1.0;
            if (xroot.Attribute("fill") != null)
            {
                string value = xroot.Attribute("fill").Value;
                if (value.StartsWith("#") == true && value.Length == 7)
                {
                    byte r = byte.Parse(value.Substring(1, 2));
                    byte g = byte.Parse(value.Substring(3, 2));
                    byte b = byte.Parse(value.Substring(5, 2));
                    fillColor = new CCColor4B(r, g, b);
                }
            }
            if (xroot.Attribute("stroke-width") != null)
            {
                strokeWidth = double.Parse(xroot.Attribute("stroke-width").Value);
            }
            if (xroot.Attribute("viewBox") != null)
            {
                string[] values = xroot.Attribute("viewBox").Value.Split(new char[] { ',', ' ' });
                if (values.Length == 4)
                {
                    contentX = double.Parse(values[0]);
                    contentY = double.Parse(values[1]);
                }
            }

            XElement[] xpaths = xroot.Elements().Where(x => x.Name.LocalName == "path").ToArray();
            string[] datas = new string[xpaths.Length];
            for (int i = 0; i < xpaths.Length; i++)
            {
                datas[i] = xpaths[i].Attribute("d").Value;
            }

            if (width == 0 || height == 0 || (width == contentWidth && height == contentHeight))
            {
                return CreatePath(0, 0, datas, contentX, contentY, contentWidth, contentHeight, fillColor, fillColor, strokeWidth, UIGraphic.Stretch.StretchNone);
            }
            return CreatePath(width, height, datas, contentX, contentY, contentWidth, contentHeight, fillColor, fillColor, strokeWidth, UIGraphic.Stretch.StretchFill);
        }
        public static CCSprite CreatePathFromSVG(string svg, double width, double height)
        {
            return CreatePathFromSVG(svg, width, height, CCColor4B.White);
        }

        public static CCSprite CreateText(string text, CCColor4B fill, CCColor4B stroke, TypeFace font, double emSizeInPoints, bool underline = false, bool flatenCurves = true, double strokeThickness = 1)
        {
            TypeFacePrinter printer = new TypeFacePrinter(text, new StyledTypeFace(font, emSizeInPoints, underline, flatenCurves));
            double width = printer.LocalBounds.Width;
            double height = printer.LocalBounds.Height;

            RectangleDouble rect = new RectangleDouble();
            bounding_rect.bounding_rect_single(printer, 0, ref rect);
            VertexSourceApplyTransform path = new VertexSourceApplyTransform(printer, Affine.NewTranslation(-rect.Left, -rect.Bottom));

            ImageBuffer buffer = new ImageBuffer((int)width, (int)height, 32, new BlenderRGBA());
            Graphics2D g = buffer.NewGraphics2D();

            if (fill.A > 0) g.Render(path, new RGBA_Bytes(fill.R, fill.G, fill.B, fill.A));
            if (stroke.A > 0) g.Render(new Stroke(path, strokeThickness), new RGBA_Bytes(stroke.R, stroke.G, stroke.B, stroke.A));

            Texture2D xnaTexture = XnaTexture((int)width, (int)height);
            xnaTexture.SetData<byte>(buffer.GetBuffer());
            CCTexture2D ccTexture = new CCTexture2D();
            ccTexture.InitWithTexture(xnaTexture);
            return new CCSprite(ccTexture);
        }
    }
#endif 

}