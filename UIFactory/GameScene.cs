using Cocos2D;
using MatterHackers.Agg.Font;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace UIFactory
{
    public class GameScene : CCScene
    {
        //private string[] svgs = System.IO.Directory.GetFiles("Content/svg", "*.svg");
        private string[] svgs = System.IO.Directory.GetFiles(@"F:\WindowsIcons-master\WindowsPhone\svg", "*.svg");
        private int svgIndex = 0;

        public GameScene() { }

        public override void OnEnter()
        {
            base.OnEnter();
            CCDirector.SharedDirector.TouchDispatcher.AddTargetedDelegate(this, this.TouchPriority, false);
        }
        public override void OnExit()
        {
            CCDirector.SharedDirector.TouchDispatcher.RemoveDelegate(this);
            base.OnExit();
        }

        public override bool TouchBegan(CCTouch t)
        {
            this.RemoveAllChildrenWithCleanup(true);
            var watch = System.Diagnostics.Stopwatch.StartNew();

            this.Schedule(dt =>
            {
                //string svg = "M32,7.174C18.311,7.174,7.174,18.311,7.174,32c0,13.689,11.137,24.826,24.826,24.826c13.689,0,24.826-11.137,24.826-24.826C56.826,18.311,45.689,7.174,32,7.174z M43.075,26.318c0.011,0.246,0.017,0.494,0.017,0.742c0,7.551-5.747,16.257-16.259,16.257c-3.227,0-6.231-0.945-8.759-2.567c0.447,0.053,0.902,0.08,1.363,0.08c2.678,0,5.141-0.914,7.097-2.446c-2.5-0.046-4.611-1.698-5.338-3.969c0.348,0.068,0.707,0.103,1.074,0.103c0.521,0,1.027-0.07,1.506-0.2c-2.614-0.525-4.583-2.834-4.583-5.602c0-0.024,0-0.049,0.001-0.072c0.77,0.427,1.651,0.685,2.587,0.714c-1.532-1.023-2.541-2.773-2.541-4.755c0-1.048,0.281-2.03,0.773-2.874c2.817,3.458,7.029,5.732,11.777,5.972c-0.098-0.419-0.147-0.854-0.147-1.303c0-3.155,2.558-5.714,5.714-5.714c1.643,0,3.128,0.694,4.17,1.804c1.303-0.256,2.525-0.73,3.63-1.387c-0.428,1.335-1.333,2.454-2.514,3.162c1.157-0.138,2.259-0.444,3.282-0.899C45.161,24.508,44.191,25.515,43.075,26.318z";
                var sprite = liwq.UIGraphic.Begin(300, 300).
                    AddRectangle(0, 0, 300, 300, liwq.Colors.SkyBlue, liwq.Colors.SkyBlue).
                    //AddEllipse(10, 10, 50, 50, liwq.Colors.Turquoise, liwq.Colors.Turquoise, 4).
                    //AddLine(10, 10, 60, 60, liwq.Colors.Lime, 4).
                    //AddPaths(200, 200, 200, 200, new string[1] { svg }, 0, 0, 64, 64, liwq.Colors.DarkOrange, liwq.Colors.White, 2).
                    //AddSvg(200, 200, 200, 200, liwq.Factory.ReadString("Content/svg/dribbble.svg")).
                    AddSvg(50, 50, 200, 200, liwq.Factory.ReadString(this.svgs[svgIndex++])).
                    AddText(60, 30, System.IO.Path.GetFileNameWithoutExtension(this.svgs[svgIndex]), liwq.Colors.DarkRed, liwq.Colors.DarkRed, MatterHackers.Agg.Font.LiberationSansFont.Instance, 16).
                    EndSprite();
                sprite.Position = CCDirector.SharedDirector.WinSize.Center;
                this.AddChild(sprite);
            }, 0.2f);

            watch.Stop();
            System.Console.WriteLine(watch.Elapsed);
            return true;
        }


        public static void Save(Texture2D texture, int width, int height, ImageFormat imageFormat, string filename)
        {
            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                byte blue;
                IntPtr safePtr;
                BitmapData bitmapData;
                Rectangle rect = new Rectangle(0, 0, width, height);
                byte[] textureData = new byte[4 * width * height];

                texture.GetData<byte>(textureData);
                for (int i = 0; i < textureData.Length; i += 4)
                {
                    blue = textureData[i];
                    textureData[i] = textureData[i + 2];
                    textureData[i + 2] = blue;
                }
                bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                safePtr = bitmapData.Scan0;
                System.Runtime.InteropServices.Marshal.Copy(textureData, 0, safePtr, textureData.Length);
                bitmap.UnlockBits(bitmapData);
                bitmap.Save(filename, imageFormat);
            }
        }
    }
}
