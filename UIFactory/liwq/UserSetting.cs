#define SETTING_ENCRYPT

using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace liwq
{
    //一个xml项对应一个字典的所有值
    public class UserSetting
    {
        //static
        private static Dictionary<string, UserSetting> _cache = new Dictionary<string, UserSetting>();

        private static UserSetting FromXml(string user)
        {
            if (_cache.ContainsKey(user) == false)
            {
                UserSetting setting = new UserSetting(user);

                string xml = Cocos2D.CCUserDefault.SharedUserDefault.GetStringForKey(user, null);
                if (xml != null)
                {
#if SETTING_ENCRYPT
                    //解密
                    //string<-byte[]<-base64<-Reverse
                    xml = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(new string(xml.Reverse().ToArray())));
                    //解密
#endif
                    XElement root = XElement.Parse(xml);
                    foreach (var item in root.Elements())
                        setting._kv.Add(item.Name.ToString(), item.Value);
                }
                _cache.Add(user, setting);
            }
            return _cache[user];
        }
        private string ToXml()
        {
            XElement root = new XElement(this._user);
            foreach (var kp in this._kv)
                root.Add(new XElement(kp.Key, kp.Value));
            return root.ToString();
        }

        public static UserSetting Get(string user)
        {
            return FromXml(user);
        }

        //not static 

        private Dictionary<string, string> _kv = new Dictionary<string, string>();
        private string _user;
        private UserSetting(string user) { this._user = user; }

        private string getValueForKey(string key)
        {
            if (this._kv.ContainsKey(key) == true)
                return this._kv[key];
            return null;
        }
        private void setValueForKey(string key, string value)
        {
            if (this._kv.ContainsKey(key) == false)
                this._kv.Add(key, value);
            else
                this._kv[key] = value;
        }

        public void Flush()
        {
            string xml = this.ToXml();
#if SETTING_ENCRYPT
            //加密
            //string->byte[]->base64->Reverse
            xml = new string(System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(xml)).Reverse().ToArray());
            //加密
#endif
            Cocos2D.CCUserDefault.SharedUserDefault.SetStringForKey(this._user, xml);
            Cocos2D.CCUserDefault.SharedUserDefault.Flush();
        }

        public bool GetBoolForKey(string pKey)
        {
            return this.GetBoolForKey(pKey, false);
        }
        public bool GetBoolForKey(string pKey, bool defaultValue)
        {
            string value = this.getValueForKey(pKey);
            if (value != null) return bool.Parse(value); ;
            return defaultValue;
        }

        public int GetIntegerForKey(string pKey)
        {
            return this.GetIntegerForKey(pKey, 0);
        }
        public int GetIntegerForKey(string pKey, int defaultValue)
        {
            string value = this.getValueForKey(pKey);
            if (value != null) return int.Parse(value);
            return defaultValue;
        }

        public float GetFloatForKey(string pKey, float defaultValue)
        {
            float ret = (float)this.GetDoubleForKey(pKey, (double)defaultValue);
            return ret;
        }
        public double GetDoubleForKey(string pKey, double defaultValue)
        {
            string value = this.getValueForKey(pKey);
            if (value != null) return double.Parse(value);
            return defaultValue;
        }

        public string GetStringForKey(string pKey, string defaultValue)
        {
            string value = this.getValueForKey(pKey);
            if (value != null) return value;
            return defaultValue;
        }

        public void SetBoolForKey(string pKey, bool value)
        {
            if (pKey == null) return;
            this.SetStringForKey(pKey, value.ToString());
        }

        public void SetIntegerForKey(string pKey, int value)
        {
            if (pKey == null) return;
            this.setValueForKey(pKey, value.ToString());
        }

        public void SetFloatForKey(string pKey, float value)
        {
            this.SetDoubleForKey(pKey, value);
        }
        public void SetDoubleForKey(string pKey, double value)
        {
            if (pKey == null) return;
            this.setValueForKey(pKey, value.ToString());
        }

        public void SetStringForKey(string pKey, string value)
        {
            if (pKey == null) return;
            this.setValueForKey(pKey, value.ToString());
        }

    }
}