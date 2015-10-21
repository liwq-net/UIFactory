#region Using Statements
using Cocos2D;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using System;
#endregion

namespace UIFactory
{
    /// <summary>
    /// This is the main type for your game.
    /// </summary>
    public class Game1 : Game
    {
        GraphicsDeviceManager graphics;
        SpriteBatch spriteBatch;

        public Game1()
        {
            graphics = new GraphicsDeviceManager(this);
            //#if MACOS
            //            Content.RootDirectory = "AngryNinjas/Content";
            //#else
            Content.RootDirectory = "Content";
            //#endif
            //
            //#if XBOX || OUYA
            //            graphics.IsFullScreen = true;
            //#else
            graphics.IsFullScreen = false;
            graphics.PreferMultiSampling = true;
            graphics.ApplyChanges();
            //#endif

            // Frame rate is 30 fps by default for Windows Phone.
            TargetElapsedTime = TimeSpan.FromTicks(333333 / 2);

            // Extend battery life under lock.
            //InactiveSleepTime = TimeSpan.FromSeconds(1);

            CCApplication application = new AppDelegate(this, graphics);
            Components.Add(application);
            //#if XBOX || OUYA
            //            CCDirector.SharedDirector.GamePadEnabled = true;
            //            application.GamePadButtonUpdate += new CCGamePadButtonDelegate(application_GamePadButtonUpdate);
            //#endif
        }

        /// <summary>
        /// Allows the game to perform any initialization it needs to before starting to run.
        /// This is where it can query for any required services and load any non-graphic
        /// related content.  Calling base.Initialize will enumerate through any components
        /// and initialize them as well.
        /// </summary>
        protected override void Initialize()
        {
            // TODO: Add your initialization logic here

            base.Initialize();

            TouchPanel.EnabledGestures = GestureType.Tap | GestureType.Flick;
        }

        public class TouchEventArgs : EventArgs
        {
            public GestureSample GestureSample { get; set; }
        }
        public event EventHandler<TouchEventArgs> TouchEvent;

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected override void LoadContent()
        {
            // Create a new SpriteBatch, which can be used to draw textures.
            spriteBatch = new SpriteBatch(GraphicsDevice);

            // TODO: use this.Content to load your game content here
        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// all content.
        /// </summary>
        protected override void UnloadContent()
        {
            // TODO: Unload any non ContentManager content here
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // TODO: Add your update logic here

            while (TouchPanel.IsGestureAvailable)
            {
                var gesture = TouchPanel.ReadGesture();
                if (gesture.GestureType == GestureType.Tap)
                {
                    if (this.TouchEvent != null) this.TouchEvent(this, new TouchEventArgs() { GestureSample = gesture });
                }
                else if (gesture.GestureType == GestureType.Flick)
                {
                    if (this.TouchEvent != null) this.TouchEvent(this, new TouchEventArgs() { GestureSample = gesture });
                }
            }

#if DEBUG   //增加键盘功能来调试
            CCDirector.SharedDirector.KeyboardDispatcher.DispatchKeyboardState();
#endif

            base.Update(gameTime);
        }

        /// <summary>
        /// This is called when the game should draw itself.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.SkyBlue);

            // TODO: Add your drawing code here

            base.Draw(gameTime);
        }
    }
}
