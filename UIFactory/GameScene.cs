using Cocos2D;

namespace UIFactory
{
    public class GameScene : CCScene
    {
        private string[] svgs = System.IO.Directory.GetFiles("Content/svg", "*.svg");
        private int svgIndex = 0;

        public GameScene()
        {
        }

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
            this.svgIndex = 0;
            this.RemoveAllChildrenWithCleanup(true);

            var watch = System.Diagnostics.Stopwatch.StartNew();

            var whiteBackgournd = liwq.UIFactory.CreateRectangle(CCDirector.SharedDirector.WinSize.Width, CCDirector.SharedDirector.WinSize.Height, 0, 0, liwq.Colors.White, liwq.Colors.Black,2);
            whiteBackgournd.Position = CCDirector.SharedDirector.WinSize.Center;
            this.AddChild(whiteBackgournd);

            for (int i = 0; i < svgs.Length - 1; i++)
            {
                try
                {
                    string svgText = liwq.Factory.ReadString(svgs[this.svgIndex++]);
                    var sprite = liwq.UIFactory.CreatePathFromSVG(svgText, 200, 200, liwq.Colors.Ramdom);
                    sprite.Position = new CCPoint(CCRandom.Next((int)CCDirector.SharedDirector.WinSize.Width), CCRandom.Next((int)CCDirector.SharedDirector.WinSize.Height));
                    this.AddChild(sprite);
                }
                catch { }
            }
            watch.Stop();
            System.Console.WriteLine(watch.Elapsed);

            return true;
        }
    }
}
