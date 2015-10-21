using Cocos2D;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace liwq
{
    #region CCBMFontConfiguration

    public class CCBMFontDef
    {
        /// <summary>ID of the character</summary>
        public int charID { get; set; }
        /// <summary>origin and size of the font</summary>
        public CCRect rect;
        /// <summary>The amount to move the current position after drawing the character (in pixels)</summary>
        public int xAdvance { get; set; }
        /// <summary>The X amount the image should be offset when drawing the image (in pixels)</summary>
        public int xOffset { get; set; }
        /// <summary>The Y amount the image should be offset when drawing the image (in pixels)</summary>
        public int yOffset { get; set; }
    }

    //internal struct CCBMFontPadding
    //{
    //    public int bottom { get; set; }
    //    public int left { get; set; }
    //    public int right { get; set; }
    //    public int top { get; set; }
    //}

    public struct CCKerningHashElement
    {
        public int amount { get; set; }
        public int key { get; set; } //key for the hash. 16-bit for 1st element, 16-bit for 2nd element
    }

    public class CCBMFontConfiguration
    {
        internal int CommonHeight { get; private set; }
        internal Dictionary<int, CCBMFontDef> FontDefDictionary { get; private set; }
        internal Dictionary<int, CCKerningHashElement> KerningDictionary { get; private set; }
        //internal CCBMFontPadding _padding;

        public string AtlasName { get; set; }
        public List<int> CharacterSet { get; private set; }

        protected virtual bool _initWithString(string fntFile)
        {
            var fileStream = Factory.OpenStrem(fntFile);
            StreamReader reader = new StreamReader(fileStream, true);
            var content = reader.ReadToEnd();
            return _initWithString(content, fntFile);
        }
        protected virtual bool _initWithString(string data, string fntFile)
        {
            this.KerningDictionary.Clear();
            this.FontDefDictionary.Clear();
            this.CharacterSet = this._parseConfigFile(data, fntFile);
            if (this.CharacterSet == null)
                return false;
            return true;
        }
        private void _purgeKerningDictionary()
        {
            this.KerningDictionary.Clear();
        }

        public CCBMFontConfiguration()
        {
            this.FontDefDictionary = new Dictionary<int, CCBMFontDef>();
            this.KerningDictionary = new Dictionary<int, CCKerningHashElement>();
            this.CharacterSet = new List<int>();
        }
        public CCBMFontConfiguration(string data, string fntFile)
        {
            this.FontDefDictionary = new Dictionary<int, CCBMFontDef>();
            this.KerningDictionary = new Dictionary<int, CCKerningHashElement>();
            this.CharacterSet = new List<int>();
            this._initWithString(data, fntFile);
        }

        public static CCBMFontConfiguration Create(string fntFile)
        {
            CCBMFontConfiguration fontConfig = new CCBMFontConfiguration();
            if (fontConfig._initWithString(fntFile))
                return fontConfig;
            return null;
        }

        private List<int> _parseConfigFile(string buffer, string fntFile)
        {
            if (string.IsNullOrEmpty(buffer))
                return null;

            var validCharsString = new List<int>();

            System.Xml.Linq.XElement xml = System.Xml.Linq.XElement.Parse(buffer);

            ////info
            //try
            //{
            //    var temp = xml.Element("info").Attribute("padding").Value.Split(',');
            //    this._padding.top = Cocos2D.CCUtils.CCParseInt(temp[0]);
            //    this._padding.right = Cocos2D.CCUtils.CCParseInt(temp[1]);
            //    this._padding.bottom = Cocos2D.CCUtils.CCParseInt(temp[2]);
            //    this._padding.left = Cocos2D.CCUtils.CCParseInt(temp[3]);
            //}
            //catch { }

            //common
            try
            {
                this.CommonHeight = Cocos2D.CCUtils.CCParseInt(xml.Element("common").Attribute("lineHeight").Value);
            }
            catch { }

            //page file
            try
            {
                this.AtlasName = xml.Element("pages").Element("page").Attribute("file").Value;
            }
            catch { }


            try
            {
                //<char id="32" x="155" y="75" width="3" height="1" xoffset="-1" yoffset="31" xadvance="8" page="0" chnl="15" />
                var chars = xml.Element("chars").Elements().ToList();
                foreach (var c in chars)
                {
                    CCBMFontDef characterDefinition = new CCBMFontDef();
                    {
                        characterDefinition.charID = Cocos2D.CCUtils.CCParseInt(c.Attribute("id").Value);
                        characterDefinition.rect.Origin.X = Cocos2D.CCUtils.CCParseInt(c.Attribute("x").Value);
                        characterDefinition.rect.Origin.Y = Cocos2D.CCUtils.CCParseInt(c.Attribute("y").Value);
                        characterDefinition.rect.Size.Width = Cocos2D.CCUtils.CCParseInt(c.Attribute("width").Value);
                        characterDefinition.rect.Size.Height = Cocos2D.CCUtils.CCParseInt(c.Attribute("height").Value);
                        characterDefinition.xOffset = Cocos2D.CCUtils.CCParseInt(c.Attribute("xoffset").Value);
                        characterDefinition.yOffset = Cocos2D.CCUtils.CCParseInt(c.Attribute("yoffset").Value);
                        characterDefinition.xAdvance = Cocos2D.CCUtils.CCParseInt(c.Attribute("xadvance").Value);
                    }
                    this.FontDefDictionary.Add(characterDefinition.charID, characterDefinition);
                    validCharsString.Add(characterDefinition.charID);
                }
            }
            catch { }

            try
            {
                //<kerning first="32" second="65" amount="-2" />
                var kernings = xml.Element("kernings").Elements().ToList();
                foreach (var k in kernings)
                {
                    CCKerningHashElement kerningHashElement = new CCKerningHashElement();
                    {
                        kerningHashElement.amount = Cocos2D.CCUtils.CCParseInt(k.Attribute("amount").Value);
                        int first = Cocos2D.CCUtils.CCParseInt(k.Attribute("first").Value);
                        int second = Cocos2D.CCUtils.CCParseInt(k.Attribute("second").Value);
                        kerningHashElement.key = (first << 16) | (second & 0xffff);
                    }
                    this.KerningDictionary.Add(kerningHashElement.key, kerningHashElement);
                }
            }
            catch { }

            return validCharsString;
        }
    }

    #endregion //CCBMFontConfiguration

    public class XBMFont : CCSpriteBatchNode, ICCLabelProtocol, ICCRGBAProtocol
    {
        public static Dictionary<string, CCBMFontConfiguration> FontConfigCache = new Dictionary<string, CCBMFontConfiguration>();

        private static CCBMFontConfiguration _fontConfigLoad(string file)
        {
            CCBMFontConfiguration config;
            if (FontConfigCache.TryGetValue(file, out config) == false)
            {
                config = CCBMFontConfiguration.Create(file);
                FontConfigCache.Add(file, config);
            }
            return config;
        }

        public const int LABEL_AUTOMATIC_WIDTH = -1;

        protected CCBMFontConfiguration _fontConfig;
        protected bool _isLabelDirty;
        protected string _text = "";
        protected CCSprite _reusedChar;
        protected CCPoint _imageOffset;

        public override CCPoint AnchorPoint
        {
            get { return base.AnchorPoint; }
            set { if (base.AnchorPoint != value) { base.AnchorPoint = value; this._isLabelDirty = true; } }
        }

        public override float Scale
        {
            get { return base.Scale; }
            set { base.Scale = value; this._isLabelDirty = true; }
        }

        public override float ScaleX
        {
            get { return base.ScaleX; }
            set { base.ScaleX = value; this._isLabelDirty = true; }
        }

        public override float ScaleY
        {
            get { return base.ScaleY; }
            set { base.ScaleY = value; this._isLabelDirty = true; }
        }

        protected CCTextAlignment _horizontalAlignment = CCTextAlignment.Center;
        public CCTextAlignment HorizontalAlignment
        {
            get { return this._horizontalAlignment; }
            set { if (this._horizontalAlignment != value) { this._horizontalAlignment = value; this._isLabelDirty = true; } }
        }

        protected CCVerticalTextAlignment _verticalAlignment = CCVerticalTextAlignment.Top;
        public CCVerticalTextAlignment VerticalAlignment
        {
            get { return this._verticalAlignment; }
            set { if (this._verticalAlignment != value) { this._verticalAlignment = value; this._isLabelDirty = true; } }
        }

        protected CCSize _dimensions;
        public CCSize Dimensions
        {
            get { return this._dimensions; }
            set { if (this._dimensions != value) { this._dimensions = value; this._isLabelDirty = true; } }
        }

        protected bool _isLineBreakWithoutSpaces;
        public bool LineBreakWithoutSpace
        {
            get { return this._isLineBreakWithoutSpaces; }
            set { this._isLineBreakWithoutSpaces = value; this._isLabelDirty = true; }
        }

        protected string _fontFile;
        public string FontFile
        {
            get { return this._fontFile; }
            set
            {
                if (value != null && this._fontFile != value)
                {
                    CCBMFontConfiguration newConfig = _fontConfigLoad(value);
                    this._fontFile = value;
                    this._fontConfig = newConfig;
                    base.Texture = CCTextureCache.SharedTextureCache.AddImage(this._fontConfig.AtlasName);
                    this._isLabelDirty = true;
                }
            }
        }

        #region ICCLabelProtocol Members

        protected string _initialText;
        public virtual string Text
        {
            get { return this._initialText; }
            set { if (this._initialText != value) { this._initialText = value; this._isLabelDirty = true; } }
        }
        public void SetString(string text) { this.Text = text; }
        public string GetString() { return this.Text; }

        #endregion

        #region ICCRGBAProtocol Members

        protected byte _displayedOpacity = 255;
        protected byte _realOpacity = 255;
        protected CCColor3B _displayedColor = CCTypes.CCWhite;
        protected CCColor3B _realColor = CCTypes.CCWhite;
        protected bool _isCascadeColorEnabled = true;
        protected bool _isCascadeOpacityEnabled = true;
        protected bool _isOpacityModifyRGB = false;

        public virtual CCColor3B Color
        {
            get { return this._realColor; }
            set
            {
                this._displayedColor = this._realColor = value;
                if (this._isCascadeColorEnabled)
                {
                    var parentColor = CCTypes.CCWhite;
                    var parent = base.Parent as ICCRGBAProtocol;
                    if (parent != null && parent.CascadeColorEnabled)
                    {
                        parentColor = parent.DisplayedColor;
                    }
                    this.UpdateDisplayedColor(parentColor);
                }
            }
        }

        public virtual CCColor3B DisplayedColor { get { return this._displayedColor; } }

        public virtual byte Opacity
        {
            get { return this._realOpacity; }
            set
            {
                this._displayedOpacity = this._realOpacity = value;
                if (this._isCascadeOpacityEnabled)
                {
                    byte parentOpacity = 255;
                    var parent = base.Parent as ICCRGBAProtocol;
                    if (parent != null && parent.CascadeOpacityEnabled)
                    {
                        parentOpacity = parent.DisplayedOpacity;
                    }
                    this.UpdateDisplayedOpacity(parentOpacity);
                }
            }
        }

        public virtual byte DisplayedOpacity { get { return this._displayedOpacity; } }

        public virtual bool IsOpacityModifyRGB
        {
            get { return this._isOpacityModifyRGB; }
            set
            {
                this._isOpacityModifyRGB = value;
                if (this.Children != null && this.Children.Count > 0)
                {
                    for (int i = 0, count = this.Children.Count; i < count; i++)
                    {
                        var rgbaProtocol = this.Children.Elements[i] as ICCRGBAProtocol;
                        if (rgbaProtocol != null)
                        {
                            rgbaProtocol.IsOpacityModifyRGB = value;
                        }
                    }
                }
            }
        }

        public virtual bool CascadeColorEnabled { get { return false; } set { this._isCascadeColorEnabled = value; } }

        public virtual bool CascadeOpacityEnabled { get { return false; } set { this._isCascadeOpacityEnabled = value; } }

        public virtual void UpdateDisplayedColor(CCColor3B parentColor)
        {
            this._displayedColor.R = (byte)(_realColor.R * parentColor.R / 255.0f);
            this._displayedColor.G = (byte)(_realColor.G * parentColor.G / 255.0f);
            this._displayedColor.B = (byte)(_realColor.B * parentColor.B / 255.0f);
            if (this.Children != null)
            {
                for (int i = 0, count = this.Children.Count; i < count; i++)
                {
                    ((CCSprite)this.Children.Elements[i]).UpdateDisplayedColor(this._displayedColor);
                }
            }
        }

        public virtual void UpdateDisplayedOpacity(byte parentOpacity)
        {
            this._displayedOpacity = (byte)(this._realOpacity * parentOpacity / 255.0f);

            if (this.Children != null)
            {
                for (int i = 0, count = this.Children.Count; i < count; i++)
                {
                    ((CCSprite)this.Children.Elements[i]).UpdateDisplayedOpacity(this._displayedOpacity);
                }
            }
        }

        #endregion

        public static void FNTConfigRemoveCache()
        {
            if (FontConfigCache != null)
            {
                FontConfigCache.Clear();
            }
        }
        public static void PurgeCachedData()
        {
            FNTConfigRemoveCache();
        }

        protected virtual bool InitWithString(string text, string fontFile, CCSize dimentions, CCTextAlignment hAlignment, CCVerticalTextAlignment vAlignment, CCPoint imageOffset, CCTexture2D texture)
        {
            if (string.IsNullOrEmpty(fontFile) == false)
            {
                CCBMFontConfiguration newConfig = _fontConfigLoad(fontFile);
                if (newConfig == null)
                    return false;

                this._fontConfig = newConfig;
                //this._fontFile = fontFile;

                if (texture == null)
                {
                    //base.Texture = CCTextureCache.SharedTextureCache.AddImage(this._configuration.AtlasName);
                    texture = Factory.CreateTexture(this._fontConfig.AtlasName);
                }
            }
            else
            {
                texture = new CCTexture2D();
            }

            if (text == null) text = "";
            if (base.InitWithTexture(texture, text.Length))
            {
                this._dimensions = dimentions;
                this._horizontalAlignment = hAlignment;
                this._verticalAlignment = vAlignment;

                this._displayedOpacity = this._realOpacity = 255;
                this._displayedColor = this._realColor = CCTypes.CCWhite;
                this._isCascadeOpacityEnabled = true;
                this._isCascadeColorEnabled = true;
                base.ContentSize = CCSize.Zero;
                this._isOpacityModifyRGB = base.TextureAtlas.Texture.HasPremultipliedAlpha;
                this.AnchorPoint = new CCPoint(0.5f, 0.5f);
                this._imageOffset = imageOffset;
                this._reusedChar = new CCSprite();
                this._reusedChar.InitWithTexture(base.TextureAtlas.Texture, CCRect.Zero, false);
                this._reusedChar.BatchNode = this;
                this.SetString(text, true);
                return true;
            }
            return false;
        }

        public override bool Init()
        {
            return InitWithString(null, null, new CCSize(LABEL_AUTOMATIC_WIDTH, 0), CCTextAlignment.Left, CCVerticalTextAlignment.Top, CCPoint.Zero, null);
        }

        public XBMFont() { Init(); }
        public XBMFont(string text, string fontFile, float width) : this(text, fontFile, width, CCTextAlignment.Left, CCPoint.Zero) { }
        public XBMFont(string text, string fontFile) : this(text, fontFile, LABEL_AUTOMATIC_WIDTH, CCTextAlignment.Left, CCPoint.Zero) { }
        public XBMFont(string text, string fontFile, float width, CCTextAlignment alignment) : this(text, fontFile, width, alignment, CCPoint.Zero) { }
        public XBMFont(string text, string fontFile, float width, CCTextAlignment alignment, CCPoint imageOffset)
        {
            this.InitWithString(text, fontFile, new CCSize(width, 0), alignment, CCVerticalTextAlignment.Top, imageOffset, null);
        }

        private int _kerningAmountForFirst(int first, int second)
        {
            int key = (first << 16) | (second & 0xffff);
            if (this._fontConfig.KerningDictionary != null)
            {
                CCKerningHashElement element;
                if (this._fontConfig.KerningDictionary.TryGetValue(key, out element))
                {
                    return element.amount;
                }
            }
            return 0;
        }

        public void CreateFontChars()
        {
            int nextFontPositionX = 0;
            int nextFontPositionY = 0;
            char prev = (char)255;
            int kerningAmount = 0;

            CCSize tmpSize = CCSize.Zero;

            int longestLine = 0;
            int totalHeight = 0;
            int quantityOfLines = 1;
            if (string.IsNullOrEmpty(this._text))
            {
                return;
            }

            int textLength = this._text.Length;
            var charSet = this._fontConfig.CharacterSet;
            if (charSet.Count == 0)
            {
                throw (new InvalidOperationException("Can not compute the size of the font because the character set is empty."));
            }

            for (int i = 0; i < textLength - 1; ++i)
            {
                if (this._text[i] == '\n')
                    quantityOfLines++;
            }

            totalHeight = this._fontConfig.CommonHeight * quantityOfLines;
            nextFontPositionY = 0 - (this._fontConfig.CommonHeight - this._fontConfig.CommonHeight * quantityOfLines);

            CCBMFontDef fontDef = null;
            CCRect rect;

            for (int i = 0; i < textLength; i++)
            {
                char c = this._text[i];
                if (c == '\n')
                {
                    nextFontPositionX = 0;
                    nextFontPositionY -= _fontConfig.CommonHeight;
                    continue;
                }

                if (charSet.IndexOf(c) == -1)
                {
                    CCLog.Log("Cocos2D.CCLabelBMFont: Attempted to use character not defined in this bitmap: {0}", (int)c);
                    continue;
                }

                kerningAmount = this._kerningAmountForFirst(prev, c);

                // unichar is a short, and an int is needed on HASH_FIND_INT
                if (this._fontConfig.FontDefDictionary.TryGetValue(c, out fontDef) == false)
                {
                    CCLog.Log("cocos2d::CCLabelBMFont: characer not found {0}", (int)c);
                    continue;
                }

                rect = fontDef.rect;
                rect = rect.PixelsToPoints();
                rect.Origin.X += _imageOffset.X;
                rect.Origin.Y += _imageOffset.Y;

                CCSprite fontChar = (CCSprite)base.GetChildByTag(i);
                if (fontChar != null)
                {
                    // Reusing previous Sprite
                    fontChar.Visible = true;
                }
                else
                {
                    fontChar = new CCSprite();
                    fontChar.InitWithTexture(base.TextureAtlas.Texture, rect);
                    AddChild(fontChar, i, i);
                    fontChar.IsOpacityModifyRGB = this._isOpacityModifyRGB;
                    fontChar.UpdateDisplayedColor(this._displayedColor);
                    fontChar.UpdateDisplayedOpacity(this._displayedOpacity);
                }
                // updating previous sprite
                fontChar.SetTextureRect(rect, false, rect.Size);

                // See issue 1343. cast( signed short + unsigned integer ) == unsigned integer (sign is lost!)
                int yOffset = this._fontConfig.CommonHeight - fontDef.yOffset;
                var fontPos = new CCPoint(
                    (float)nextFontPositionX + fontDef.xOffset + fontDef.rect.Size.Width * 0.5f + kerningAmount,
                    (float)nextFontPositionY + yOffset - rect.Size.Height * 0.5f * CCMacros.CCContentScaleFactor()
                    );
                fontChar.Position = fontPos.PixelsToPoints();

                // update kerning
                nextFontPositionX += fontDef.xAdvance + kerningAmount;
                prev = c;

                if (longestLine < nextFontPositionX)
                {
                    longestLine = nextFontPositionX;
                }
            }

            // If the last character processed has an xAdvance which is less that the width of the characters image,
            // then we need to adjust the width of the string to take this into account, 
            // or the character will overlap the end of the bounding box
            if (fontDef.xAdvance < fontDef.rect.Size.Width)
            {
                tmpSize.Width = longestLine + fontDef.rect.Size.Width - fontDef.xAdvance;
            }
            else
            {
                tmpSize.Width = longestLine;
            }
            tmpSize.Height = totalHeight;
            tmpSize = new CCSize(
                this._dimensions.Width > 0 ? this._dimensions.Width : tmpSize.Width,
                this._dimensions.Height > 0 ? this._dimensions.Height : tmpSize.Height
                );
            ContentSize = tmpSize.PixelsToPoints();
        }

        public virtual void SetString(string text, bool needUpdateLabel)
        {
            if (needUpdateLabel == false)
                this._text = text;
            else
                this._initialText = text;
            this._updateString(needUpdateLabel);
        }

        private void _updateString(bool needUpdateLabel)
        {
            if (this.Children != null && this.Children.Count != 0)
            {
                CCNode[] elements = this.Children.Elements;
                for (int i = 0, count = base.Children.Count; i < count; i++)
                {
                    elements[i].Visible = false;
                }
            }
            this.CreateFontChars();
            if (needUpdateLabel)
            {
                this._updateLabel();
            }
        }

        protected void _updateLabel()
        {
            this.SetString(this._initialText, false);

            if (this._text == null)
            {
                return;
            }
            if (this._dimensions.Width > 0)
            {
                // Step 1: Make multiline
                string alltext = this._text;
                int textLength = alltext.Length;
                StringBuilder multilines = new StringBuilder(textLength);
                StringBuilder lastWord = new StringBuilder(textLength);

                int line = 1;
                int i = 0;
                bool isStartLine = false;
                bool isStartWord = false;
                float startOfLine = -1;
                float startOfWord = -1;
                int skip = 0;

                CCRawList<CCNode> children = base.Children;
                for (int j = 0; j < children.Count; j++)
                {
                    CCSprite characterSprite;
                    int justSkipped = 0;
                    while ((characterSprite = (CCSprite)GetChildByTag(j + skip + justSkipped)) == null)
                    {
                        justSkipped++;
                    }
                    skip += justSkipped;

                    if (characterSprite.Visible == false)
                        continue;

                    if (i >= textLength)
                        break;

                    char character = alltext[i];

                    if (isStartWord == false)
                    {
                        startOfWord = GetLetterPosXLeft(characterSprite);
                        isStartWord = true;
                    }
                    if (isStartLine == false)
                    {
                        startOfLine = startOfWord;
                        isStartLine = true;
                    }

                    // Newline.
                    if (character == '\n')
                    {
                        int len = lastWord.Length;
                        while (len > 0 && Char.IsWhiteSpace(lastWord[len - 1]))
                        {
                            len--;
                            lastWord.Remove(len, 1);
                        }

                        multilines.Append(lastWord);
                        multilines.Append('\n');
                        lastWord.Clear();
                        isStartWord = false;
                        isStartLine = false;
                        startOfWord = -1;
                        startOfLine = -1;
                        i += justSkipped;
                        line++;

                        if (i >= textLength)
                            break;

                        character = alltext[i];

                        if (startOfWord == 0)
                        {
                            startOfWord = GetLetterPosXLeft(characterSprite);
                            isStartWord = true;
                        }
                        if (startOfLine == 0)
                        {
                            startOfLine = startOfWord;
                            isStartLine = true;
                        }
                    }

                    // Whitespace.
                    if (Char.IsWhiteSpace(character))
                    {
                        lastWord.Append(character);
                        multilines.Append(lastWord);
                        lastWord.Clear();
                        isStartWord = false;
                        startOfWord = -1;
                        i++;
                        continue;
                    }

                    // Out of bounds.
                    if (GetLetterPosXRight(characterSprite) - startOfLine > _dimensions.Width)
                    {
                        if (!_isLineBreakWithoutSpaces)
                        {
                            lastWord.Append(character);

                            int len = multilines.Length;
                            while (len > 0 && Char.IsWhiteSpace(multilines[len - 1]))
                            {
                                len--;
                                multilines.Remove(len, 1);
                            }

                            if (multilines.Length > 0)
                            {
                                multilines.Append('\n');
                            }

                            line++;
                            isStartLine = false;
                            startOfLine = -1;
                            i++;
                        }
                        else
                        {
                            int len = lastWord.Length;
                            while (len > 0 && Char.IsWhiteSpace(lastWord[len - 1]))
                            {
                                len--;
                                lastWord.Remove(len, 1);
                            }

                            multilines.Append(lastWord);
                            multilines.Append('\n');
                            lastWord.Clear();
                            isStartWord = false;
                            isStartLine = false;
                            startOfWord = -1;
                            startOfLine = -1;
                            line++;

                            if (i >= textLength)
                                break;

                            if (startOfWord == 0)
                            {
                                startOfWord = GetLetterPosXLeft(characterSprite);
                                isStartWord = true;
                            }
                            if (startOfLine == 0)
                            {
                                startOfLine = startOfWord;
                                isStartLine = true;
                            }

                            j--;
                        }

                        continue;
                    }
                    else
                    {
                        // Character is normal.
                        lastWord.Append(character);
                        i++;
                        continue;
                    }
                }

                multilines.Append(lastWord);
                SetString(multilines.ToString(), false);
            }

            // Step 2: Make alignment
            if (this._horizontalAlignment != CCTextAlignment.Left)
            {
                int i = 0;
                int lineNumber = 0;
                int textLength = this._text.Length;
                var lastLine = new CCRawList<char>();
                for (int ctr = 0; ctr <= textLength; ++ctr)
                {
                    if (ctr == textLength || this._text[ctr] == '\n')
                    {
                        float lineWidth = 0.0f;
                        int lineLength = lastLine.Count;
                        if (lineLength == 0)
                        {
                            lineNumber++;
                            continue;
                        }
                        int index = i + lineLength - 1 + lineNumber;
                        if (index < 0) continue;

                        var lastChar = (CCSprite)GetChildByTag(index);
                        if (lastChar == null)
                            continue;

                        lineWidth = lastChar.Position.X + lastChar.ContentSize.Width / 2.0f;
                        float shift = 0;
                        switch (this._horizontalAlignment)
                        {
                            case CCTextAlignment.Center:
                                shift = ContentSize.Width / 2.0f - lineWidth / 2.0f;
                                break;
                            case CCTextAlignment.Right:
                                shift = ContentSize.Width - lineWidth;
                                break;
                            default:
                                break;
                        }

                        if (shift != 0)
                        {
                            for (int j = 0; j < lineLength; j++)
                            {
                                index = i + j + lineNumber;
                                if (index < 0) continue;

                                var characterSprite = (CCSprite)GetChildByTag(index);
                                characterSprite.Position = characterSprite.Position + new CCPoint(shift, 0.0f);
                            }
                        }

                        i += lineLength;
                        lineNumber++;

                        lastLine.Clear();
                        continue;
                    }

                    lastLine.Add(_text[ctr]);
                }
            }

            if (this._verticalAlignment != CCVerticalTextAlignment.Bottom && this._dimensions.Height > 0)
            {
                int lineNumber = 1;
                int textLength = this._text.Length;
                for (int ctr = 0; ctr < textLength; ++ctr)
                {
                    if (this._text[ctr] == '\n')
                    {
                        lineNumber++;
                    }
                }

                float yOffset = 0;

                if (this._verticalAlignment == CCVerticalTextAlignment.Center)
                {
                    yOffset = this._dimensions.Height / 2f - (this._fontConfig.CommonHeight * lineNumber) / 2f;
                }
                else
                {
                    yOffset = this._dimensions.Height - this._fontConfig.CommonHeight * lineNumber;
                }

                for (int i = 0; i < textLength; i++)
                {
                    var characterSprite = GetChildByTag(i);
                    characterSprite.PositionY += yOffset;
                }
            }
        }

        private float GetLetterPosXLeft(CCSprite sp)
        {
            return sp.Position.X * base.ScaleX - (sp.ContentSize.Width * base.ScaleX * sp.AnchorPoint.X);
        }
        private float GetLetterPosXRight(CCSprite sp)
        {
            return sp.Position.X * base.ScaleX + (sp.ContentSize.Width * base.ScaleX * sp.AnchorPoint.X);
        }

        public override void Draw()
        {
            if (this._isLabelDirty)
            {
                this._updateLabel();
                this._isLabelDirty = false;
            }
            base.Draw();
        }
    }
}
