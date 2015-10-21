//CCTexture2D.InitWithTexture会默认打开premultipliedAlpha，图片边缘会出现问题。
#define TEXTURE_PACK

using System;
using Cocos2D;
using System.Collections.Generic;
using System.IO;

namespace liwq
{
    public class Factory
    {
        static Factory()
        {
            //改为在Loading场景里面加载了
//#if TEXTURE_PACK
//            CCSpriteFrameCache.SharedSpriteFrameCache.AddSpriteFramesWithFile(Factory.OpenStrem("Content/1.plist"), CreateTexture(Factory.OpenStrem("Content/1.png")));
//            CCSpriteFrameCache.SharedSpriteFrameCache.AddSpriteFramesWithFile(Factory.OpenStrem("Content/2.plist"), CreateTexture(Factory.OpenStrem("Content/2.png")));
//            CCSpriteFrameCache.SharedSpriteFrameCache.AddSpriteFramesWithFile(Factory.OpenStrem("Content/3.plist"), CreateTexture(Factory.OpenStrem("Content/3.png")));
//            CCSpriteFrameCache.SharedSpriteFrameCache.AddSpriteFramesWithFile(Factory.OpenStrem("Content/4.plist"), CreateTexture(Factory.OpenStrem("Content/4.png")));
//            CCSpriteFrameCache.SharedSpriteFrameCache.AddSpriteFramesWithFile(Factory.OpenStrem("Content/5.plist"), CreateTexture(Factory.OpenStrem("Content/5.png")));
//            CCSpriteFrameCache.SharedSpriteFrameCache.AddSpriteFrame(Factory.CreateSpriteFrame(Factory.OpenStrem("Content/font_0.png")), "font_0.png");
//            CCSpriteFrameCache.SharedSpriteFrameCache.AddSpriteFrame(Factory.CreateSpriteFrame(Factory.OpenStrem("Content/font1_0.png")), "font1_0.png");
//            CCSpriteFrameCache.SharedSpriteFrameCache.AddSpriteFrame(Factory.CreateSpriteFrame(Factory.OpenStrem("Content/font2_0.png")), "font2_0.png");
//#endif
        }

        public static Stream OpenStrem(string path)
        {
#if ANDROID
            //var astream = Game1.Activity.Assets.Open(path);
            //int b = 0;
            //MemoryStream mstream = new MemoryStream();
            //while ((b = astream.ReadByte()) != -1)
            //{
            //    mstream.WriteByte((byte)b);
            //}
            //mstream.Seek(0, SeekOrigin.Begin);
            //return mstream;

            var astream = ftdd.Game1.Activity.Assets.Open(path);
            byte[] buffer = new byte[1024 * 8];
            int count = 0;
            MemoryStream mstream = new MemoryStream();
            while ((count = astream.Read(buffer, 0, buffer.Length)) > 0)
            {
                mstream.Write(buffer, 0, count);
            }
            mstream.Seek(0, SeekOrigin.Begin);
            return mstream;
#else
            return File.OpenRead(path);
#endif
        }

        public static Cocos2D.CCTexture2D CreateTextureJpeg(Stream stream)
        {
            JpgDecoder jpg = new JpgDecoder();
            byte[] colors = jpg.Decode(stream);
            CCTexture2D cctexture = new CCTexture2D();
            cctexture.InitWithRawData<byte>(colors, Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color, jpg.Width, jpg.Height, false);
            return cctexture;
        }
        public static Cocos2D.CCTexture2D CreateTexture(Stream stream)
        {
            PngDecoder png = new PngDecoder();
            byte[] colors = png.Decode(stream);
            CCTexture2D cctexture = new CCTexture2D();
            cctexture.InitWithRawData<byte>(colors, Microsoft.Xna.Framework.Graphics.SurfaceFormat.Color, png.Width, png.Height, false);
            return cctexture;
        }
        public static Cocos2D.CCSprite CreateSprite(Stream stream)
        {
            CCTexture2D cctexture = CreateTexture(stream);
            return new CCSprite(cctexture);
        }
        public static Cocos2D.CCSpriteFrame CreateSpriteFrame(Stream stream)
        {
            CCTexture2D cctexture = CreateTexture(stream);
            return new CCSpriteFrame(cctexture, new CCRect(0, 0, cctexture.ContentSize.Width, cctexture.ContentSize.Height));
        }

#if TEXTURE_PACK
        public static Cocos2D.CCTexture2D CreateTexture(string filename, string path = null)
        {
            CCSpriteFrame frame = CCSpriteFrameCache.SharedSpriteFrameCache.SpriteFrameByName(filename);
            return frame.Texture;
        }
        public static Cocos2D.CCSprite CreateSprite(string filename, string path = null)
        {
            CCSpriteFrame frame = CCSpriteFrameCache.SharedSpriteFrameCache.SpriteFrameByName(filename);
            return new CCSprite(frame);
        }
        public static Cocos2D.CCSpriteFrame CreateSpriteFrame(string filename, string path = null)
        {
            CCSpriteFrame frame = CCSpriteFrameCache.SharedSpriteFrameCache.SpriteFrameByName(filename);
            return frame;
        }
#else
        public static Cocos2D.CCTexture2D CreateTexture(string filename, string path = null)
        {
            if (path == null) path = "Content/" + filename;
            else path = "Content/" + path + "/" + filename;
            var stream = OpenStrem(path);
            return CreateTexture(stream);
        }
        public static Cocos2D.CCSprite CreateSprite(string filename, string path = null)
        {
            if (path == null) path = "Content/" + filename;
            else path = "Content/" + path + "/" + filename;
            var stream = OpenStrem(path);
            return CreateSprite(stream);
        }
        public static Cocos2D.CCSpriteFrame CreateSpriteFrame(string filename, string path = null)
        {
            if (path == null) path = "Content/" + filename;
            else path = "Content/" + path + "/" + filename;
            var stream = OpenStrem(path);
            return CreateSpriteFrame(stream);
        }
#endif

        public static CCAnimate CreateAnimate(string[] spriteFrmaeName, float delay, uint loops = 0)
        {
            List<CCSpriteFrame> frames = new List<CCSpriteFrame>();
            for (int i = 0; i < spriteFrmaeName.Length; i++)
                frames.Add(Factory.CreateSpriteFrame(spriteFrmaeName[i]));
            CCAnimation animation = new CCAnimation(frames, delay);
            if (loops != 0) animation.Loops = loops;
            CCAnimate animate = new CCAnimate(animation);
            return animate;
        }
        public static CCAnimate CreateAnimateLoops(string[] spriteFrmaeName, float delay)
        {
            return CreateAnimate(spriteFrmaeName, delay, uint.MaxValue);
        }

        public static string ReadString(string path)
        {
            var stream = Factory.OpenStrem(path);
            StreamReader reader = new StreamReader(stream);
            string content = reader.ReadToEnd();
            reader.Close();
            return content;
        }

    }
}
