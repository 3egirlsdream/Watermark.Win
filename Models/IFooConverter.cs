using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace Watermark.Win.Models
{
    public class IWMConverter : JsonConverter
    {
        //是否开启自定义反序列化，默认值为true时，反序列化时会走ReadJson方法，值为false时，不走ReadJson方法，而是默认的反序列化
        public override bool CanRead => true;
        //是否开启自定义序列化，默认值为true时，序列化时会走WriteJson方法，值为false时，不走WriteJson方法，而是默认的序列化
        public override bool CanWrite => true;

        public override bool CanConvert(Type objectType)
        {
            return true;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            //获取JObject对象，该对象对应着我们要反序列化的json
            var jobj = serializer.Deserialize<JObject>(reader);
            //从JObject对象中获取键位ID的值
            var id = jobj.Value<int>("IWMType");
            //根据id值判断，进行赋值操作
            if (id == 1)
            {
                var model = serializer.Deserialize<WMContainer> (reader);
                return model;
            }
            else if(id == 2) //logo
            {
                var model = serializer.Deserialize<WMLogo>(reader);
                return model;
            }
            else if (id == 3) //line
            {
                var model = serializer.Deserialize<WMLine>(reader);
                return model;
            }
            else //text
            {
                var model = serializer.Deserialize<WMText>(reader);
                return model;
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
        }
    }
}
