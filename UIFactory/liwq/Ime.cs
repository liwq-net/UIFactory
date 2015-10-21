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
        //android:inputType="none"--������ͨ�ַ�
        //android:inputType="text"--������ͨ�ַ�
        //android:inputType="textCapCharacters"--������ͨ�ַ�
        //android:inputType="textCapWords"--��������ĸ��С
        //android:inputType="textCapSentences"--����һ����ĸ��С
        //android:inputType="textAutoCorrect"--ǰ�����Զ����
        //android:inputType="textAutoComplete"--ǰ�����Զ����
        //android:inputType="textMultiLine"--��������
        //android:inputType="textImeMultiLine"--���뷨���У���һ��֧�֣�
        //android:inputType="textNoSuggestions"--����ʾ
        //android:inputType="textUri"--URI��ʽ
        //android:inputType="textEmailAddress"--�����ʼ���ַ��ʽ
        //android:inputType="textEmailSubject"--�ʼ������ʽ
        //android:inputType="textShortMessage"--����Ϣ��ʽ
        //android:inputType="textLongMessage"--����Ϣ��ʽ
        //android:inputType="textPersonName"--������ʽ
        //android:inputType="textPostalAddress"--������ʽ
        //android:inputType="textPassword"--�����ʽ
        //android:inputType="textVisiblePassword"--����ɼ���ʽ
        //android:inputType="textWebEditText"--��Ϊ��ҳ�����ı���ʽ
        //android:inputType="textFilter"--�ı�ɸѡ��ʽ
        //android:inputType="textPhonetic"--ƴ�������ʽ
        //android:inputType="number"--���ָ�ʽ
        //android:inputType="numberSigned"--�з������ָ�ʽ
        //android:inputType="numberDecimal"--���Դ�С����ĸ����ʽ
        //android:inputType="phone"--���ż���
        //android:inputType="datetime"
        //android:inputType="date"--���ڼ���
        //android:inputType="time"--ʱ�����

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