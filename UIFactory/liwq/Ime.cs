#if ANDROID
using Android.App;
using Android.Widget;
using Microsoft.Xna.Framework;
using System;
using System.Threading;

namespace liwq
{
    public class Ime
    {
        //android:inputType="none"--输入普通字符
        //android:inputType="text"--输入普通字符
        //android:inputType="textCapCharacters"--输入普通字符
        //android:inputType="textCapWords"--单词首字母大小
        //android:inputType="textCapSentences"--仅第一个字母大小
        //android:inputType="textAutoCorrect"--前两个自动完成
        //android:inputType="textAutoComplete"--前两个自动完成
        //android:inputType="textMultiLine"--多行输入
        //android:inputType="textImeMultiLine"--输入法多行（不一定支持）
        //android:inputType="textNoSuggestions"--不提示
        //android:inputType="textUri"--URI格式
        //android:inputType="textEmailAddress"--电子邮件地址格式
        //android:inputType="textEmailSubject"--邮件主题格式
        //android:inputType="textShortMessage"--短消息格式
        //android:inputType="textLongMessage"--长消息格式
        //android:inputType="textPersonName"--人名格式
        //android:inputType="textPostalAddress"--邮政格式
        //android:inputType="textPassword"--密码格式
        //android:inputType="textVisiblePassword"--密码可见格式
        //android:inputType="textWebEditText"--作为网页表单的文本格式
        //android:inputType="textFilter"--文本筛选格式
        //android:inputType="textPhonetic"--拼音输入格式
        //android:inputType="number"--数字格式
        //android:inputType="numberSigned"--有符号数字格式
        //android:inputType="numberDecimal"--可以带小数点的浮点格式
        //android:inputType="phone"--拨号键盘
        //android:inputType="datetime"
        //android:inputType="date"--日期键盘
        //android:inputType="time"--时间键盘

        public static void ShowKeyboardInput(string title, string description, string defaultText, Action<string> callback, string inputType = "")
        {
            Game.Activity.RunOnUiThread(() =>
            {
                var alert = new AlertDialog.Builder(Game.Activity);

                alert.SetTitle(title);
                alert.SetMessage(description);

                var input = new EditText(Game.Activity) { Text = defaultText };
                if (defaultText != null)
                {
                    input.SetSelection(defaultText.Length);
                }
                if (inputType == "textPassword")
                {
                    input.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;
                }
                else if(inputType == "number")
                {
                    input.InputType = Android.Text.InputTypes.ClassNumber;
                }
                alert.SetView(input);

                alert.SetPositiveButton("Ok", (dialog, whichButton) =>
                {
                    if (callback != null)
                        callback(input.Text);
                });

                alert.SetNegativeButton("Cancel", (dialog, whichButton) =>
                {
                    if (callback != null)
                        callback(null);
                });
                alert.SetCancelable(false);
                alert.Show();
            });
        }
    }
}
#else
using System;
using System.Drawing;
using System.Windows.Forms;

namespace liwq
{
    public class Ime
    {
        public static void ShowKeyboardInput(string title, string description, string defaultText, Action<string> callback, string inputType = "")
        {
            Label DescriptionLabel = new Label()
            {
                AutoSize = true,
                Font = new Font("SimSun", 14.25F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134))),
                Location = new Point(6, 9),
                Size = new Size(169, 19),
                Text = description
            };
            TextBox InputTextBox = new TextBox()
            {
                Font = new Font("SimSun", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134))),
                Location = new Point(10, 46),
                Size = new Size(262, 26),
                Text = defaultText,
                SelectionStart = 0,
                SelectionLength = defaultText.Length
            };
            Button LeftButton = new Button()
            {
                Font = new Font("SimSun", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134))),
                Location = new Point(10, 109),
                Size = new Size(128, 40),
                Text = "OK"
            };
            Button RightButton = new Button()
            {
                Font = new Font("SimSun", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134))),
                Location = new Point(144, 109),
                Size = new Size(128, 40),
                Text = "Cancel"
            };
            Form InputForm = new Form()
            {
                AutoScaleDimensions = new SizeF(6F, 12F),
                AutoScaleMode = AutoScaleMode.Font,
                ClientSize = new Size(284, 161),
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            InputForm.Controls.Add(DescriptionLabel);
            InputForm.Controls.Add(InputTextBox);
            InputForm.Controls.Add(LeftButton);
            InputForm.Controls.Add(RightButton);
            LeftButton.Click += (s, e) => { InputForm.Close(); callback(InputTextBox.Text); };
            RightButton.Click += (s, e) => { InputForm.Close(); callback(InputTextBox.Text); };
            InputForm.StartPosition = FormStartPosition.CenterParent;
            InputForm.ShowDialog();
        }
    }
}
#endif