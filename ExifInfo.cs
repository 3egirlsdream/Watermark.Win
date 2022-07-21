using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JointWatermark
{
    /// <summary>
    /// 功能：获得图片EXIF信息
    /// 作者：Rexsp
    /// 创建日期：2004-03-20
    /// 作者MSN：yubo@x263.net
    /// </summary>
    public class EXIFMetaData
    {
        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        public EXIFMetaData()
        {
        }
        #endregion

        #region 数据转换结构
        /// <summary>
        /// 转换数据结构
        /// </summary>
        public struct MetadataDetail
        {
            public string Hex;//十六进制字符串
            public string RawValueAsString;//原始值串
            public string DisplayValue;//显示值串
        }
        #endregion

        #region EXIF元素结构
        /// <summary>
        /// 结构：存储EXIF元素信息
        /// </summary>
        public struct Metadata
        {
            public MetadataDetail EquipmentMake;
            public MetadataDetail CameraModel;
            public MetadataDetail ExposureTime;//曝光时间
            public MetadataDetail Fstop;
            public MetadataDetail DatePictureTaken;
            public MetadataDetail ShutterSpeed;// 快门速度
            public MetadataDetail MeteringMode;//曝光模式
            public MetadataDetail Flash;//闪光灯
            public MetadataDetail XResolution;
            public MetadataDetail YResolution;
            public MetadataDetail ImageWidth;//照片宽度
            public MetadataDetail ImageHeight;//照片高度

            public MetadataDetail FNumber;// added f值，光圈数
            public MetadataDetail ExposureProg;// added 曝光程序
            public MetadataDetail SpectralSense;// added 
            public MetadataDetail ISOSpeed;// added ISO感光度
            public MetadataDetail OECF;// added 
            public MetadataDetail Ver;// added EXIF版本
            public MetadataDetail CompConfig;// added 色彩设置
            public MetadataDetail CompBPP;// added 压缩比率
            public MetadataDetail Aperture;// added 光圈值
            public MetadataDetail Brightness;// added 亮度值Ev
            public MetadataDetail ExposureBias;// added 曝光补偿
            public MetadataDetail MaxAperture;// added 最大光圈值

            public MetadataDetail SubjectDist;// added主体距离
            public MetadataDetail LightSource;// added 白平衡
            public MetadataDetail FocalLength;// added 焦距
            public MetadataDetail FPXVer;// added FlashPix版本
            public MetadataDetail ColorSpace;// added 色彩空间
            public MetadataDetail Interop;// added 
            public MetadataDetail FlashEnergy;// added 
            public MetadataDetail SpatialFR;// added 
            public MetadataDetail FocalXRes;// added 
            public MetadataDetail FocalYRes;// added 
            public MetadataDetail FocalResUnit;// added 
            public MetadataDetail ExposureIndex;// added 曝光指数
            public MetadataDetail SensingMethod;// added 感应方式
            public MetadataDetail SceneType;// added 
            public MetadataDetail CfaPattern;// added 
        }
        #endregion

        #region 查找EXIF元素值
        public string LookupEXIFValue(string Description, string Value)
        {
            string DescriptionValue = null;

            switch (Description)
            {
                case "MeteringMode":

                    #region  MeteringMode
                    {
                        switch (Value)
                        {
                            case "0":
                                DescriptionValue = "Unknown"; break;
                            case "1":
                                DescriptionValue = "Average"; break;
                            case "2":
                                DescriptionValue = "Center Weighted Average"; break;
                            case "3":
                                DescriptionValue = "Spot"; break;
                            case "4":
                                DescriptionValue = "Multi-spot"; break;
                            case "5":
                                DescriptionValue = "Multi-segment"; break;
                            case "6":
                                DescriptionValue = "Partial"; break;
                            case "255":
                                DescriptionValue = "Other"; break;
                        }
                    }
                    #endregion

                    break;
                case "ResolutionUnit":

                    #region ResolutionUnit
                    {
                        switch (Value)
                        {
                            case "1":
                                DescriptionValue = "No Units"; break;
                            case "2":
                                DescriptionValue = "Inch"; break;
                            case "3":
                                DescriptionValue = "Centimeter"; break;
                        }
                    }

                    #endregion

                    break;
                case "Flash":

                    #region Flash
                    {
                        switch (Value)
                        {
                            case "0":
                                DescriptionValue = "未使用"; break;
                            case "1":
                                DescriptionValue = "闪光"; break;
                            case "5":
                                DescriptionValue = "Flash fired but strobe return light not detected"; break;
                            case "7":
                                DescriptionValue = "Flash fired and strobe return light detected"; break;
                        }
                    }
                    #endregion

                    break;
                case "ExposureProg":

                    #region ExposureProg
                    {
                        switch (Value)
                        {
                            case "0":
                                DescriptionValue = "没有定义"; break;
                            case "1":
                                DescriptionValue = "手动控制"; break;
                            case "2":
                                DescriptionValue = "程序控制"; break;
                            case "3":
                                DescriptionValue = "光圈优先"; break;
                            case "4":
                                DescriptionValue = "快门优先"; break;
                            case "5":
                                DescriptionValue = "夜景模式"; break;
                            case "6":
                                DescriptionValue = "运动模式"; break;
                            case "7":
                                DescriptionValue = "肖像模式"; break;
                            case "8":
                                DescriptionValue = "风景模式"; break;
                            case "9":
                                DescriptionValue = "保留的"; break;
                        }
                    }

                    #endregion

                    break;
                case "CompConfig":

                    #region CompConfig
                    {
                        switch (Value)
                        {

                            case "513":
                                DescriptionValue = "YCbCr"; break;
                        }
                    }
                    #endregion

                    break;
                case "Aperture":

                    #region Aperture
                    DescriptionValue = Value;
                    #endregion

                    break;
                case "LightSource":

                    #region LightSource
                    {
                        switch (Value)
                        {
                            case "0":
                                DescriptionValue = "未知"; break;
                            case "1":
                                DescriptionValue = "日光"; break;
                            case "2":
                                DescriptionValue = "荧光灯"; break;
                            case "3":
                                DescriptionValue = "白炽灯"; break;
                            case "10":
                                DescriptionValue = "闪光灯"; break;
                            case "17":
                                DescriptionValue = "标准光A"; break;
                            case "18":
                                DescriptionValue = "标准光B"; break;
                            case "19":
                                DescriptionValue = "标准光C"; break;
                            case "20":
                                DescriptionValue = "标准光D55"; break;
                            case "21":
                                DescriptionValue = "标准光D65"; break;
                            case "22":
                                DescriptionValue = "标准光D75"; break;
                            case "255":
                                DescriptionValue = "其它"; break;
                        }
                    }


                    #endregion
                    break;

            }
            return DescriptionValue;
        }
        #endregion

        #region 取得图片的EXIF信息
        public Metadata GetEXIFMetaData(string PhotoName)
        {
            // 创建一个图片的实例
            System.Drawing.Image MyImage = System.Drawing.Image.FromFile(PhotoName);
            // 创建一个整型数组来存储图像中属性数组的ID
            int[] MyPropertyIdList = MyImage.PropertyIdList;
            //创建一个封闭图像属性数组的实例
            PropertyItem[] MyPropertyItemList = new PropertyItem[MyPropertyIdList.Length];
            //创建一个图像EXIT信息的实例结构对象，并且赋初值

            #region 创建一个图像EXIT信息的实例结构对象，并且赋初值
            Metadata MyMetadata = new Metadata();
            MyMetadata.EquipmentMake.Hex = "10f";
            MyMetadata.CameraModel.Hex = "110";
            MyMetadata.DatePictureTaken.Hex    = "9003";
            MyMetadata.ExposureTime.Hex    = "829a";
            MyMetadata.Fstop.Hex = "829d";
            MyMetadata.ShutterSpeed.Hex = "9201";
            MyMetadata.MeteringMode.Hex = "9207";
            MyMetadata.Flash.Hex = "9209";
            MyMetadata.FNumber.Hex = "829d"; //added 
            MyMetadata.ExposureProg.Hex = ""; //added 
            MyMetadata.SpectralSense.Hex = "8824"; //added 
            MyMetadata.ISOSpeed.Hex = "8827"; //added 
            MyMetadata.OECF.Hex = "8828"; //added 
            MyMetadata.Ver.Hex = "9000"; //added 
            MyMetadata.CompConfig.Hex = "9101"; //added 
            MyMetadata.CompBPP.Hex = "9102"; //added 
            MyMetadata.Aperture.Hex = "9202"; //added 
            MyMetadata.Brightness.Hex = "9203"; //added 
            MyMetadata.ExposureBias.Hex = "9204"; //added 
            MyMetadata.MaxAperture.Hex = "9205"; //added 
            MyMetadata.SubjectDist.Hex = "9206"; //added 
            MyMetadata.LightSource.Hex = "9208"; //added 
            MyMetadata.FocalLength.Hex = "920a"; //added 
            MyMetadata.FPXVer.Hex = "a000"; //added 
            MyMetadata.ColorSpace.Hex = "a001"; //added 
            MyMetadata.FocalXRes.Hex = "a20e"; //added 
            MyMetadata.FocalYRes.Hex = "a20f"; //added 
            MyMetadata.FocalResUnit.Hex = "a210"; //added 
            MyMetadata.ExposureIndex.Hex = "a215"; //added 
            MyMetadata.SensingMethod.Hex = "a217"; //added 
            MyMetadata.SceneType.Hex = "a301";
            MyMetadata.CfaPattern.Hex = "a302";
            #endregion

            // ASCII编码
            System.Text.ASCIIEncoding Value = new System.Text.ASCIIEncoding();

            int index = 0;
            int MyPropertyIdListCount = MyPropertyIdList.Length;
            if (MyPropertyIdListCount!=0)
            {
                foreach (int MyPropertyId in MyPropertyIdList)
                {
                    string hexVal = "";
                    MyPropertyItemList[index] = MyImage.GetPropertyItem(MyPropertyId);

                    #region 初始化各属性值
                    string myPropertyIdString = MyImage.GetPropertyItem(MyPropertyId).Id.ToString("x");
                    switch (myPropertyIdString)
                    {
                        case "10f":
                            {
                                MyMetadata.EquipmentMake.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.EquipmentMake.DisplayValue = Value.GetString(MyPropertyItemList[index].Value);
                                break;
                            }

                        case "110":
                            {
                                MyMetadata.CameraModel.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.CameraModel.DisplayValue =Value.GetString(MyPropertyItemList[index].Value);
                                break;

                            }

                        case "9003":
                            {
                                MyMetadata.DatePictureTaken.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.DatePictureTaken.DisplayValue =Value.GetString(MyPropertyItemList[index].Value);
                                break;
                            }

                        case "9207":
                            {
                                MyMetadata.MeteringMode.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.MeteringMode.DisplayValue = LookupEXIFValue("MeteringMode", BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString());
                                break;
                            }

                        case "9209":
                            {
                                MyMetadata.Flash.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.Flash.DisplayValue = LookupEXIFValue("Flash", BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString());
                                break;
                            }

                        case "829a":
                            {
                                MyMetadata.ExposureTime.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                string StringValue = "";
                                for (int Offset = 0; Offset < MyImage.GetPropertyItem(MyPropertyId).Len; Offset = Offset + 4)
                                {
                                    StringValue += BitConverter.ToInt32(MyImage.GetPropertyItem(MyPropertyId).Value, Offset).ToString() + "/";
                                }
                                MyMetadata.ExposureTime.DisplayValue =StringValue.Substring(0, StringValue.Length-1);
                                break;
                            }
                        case "829d":
                            {
                                MyMetadata.Fstop.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                int int1;
                                int int2;
                                int1 =BitConverter.ToInt32(MyImage.GetPropertyItem(MyPropertyId).Value, 0);
                                int2 = BitConverter.ToInt32(MyImage.GetPropertyItem(MyPropertyId).Value, 4);
                                MyMetadata.Fstop.DisplayValue = "F/" +(int1/int2);

                                MyMetadata.FNumber.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.FNumber.DisplayValue = BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString();

                                break;
                            }
                        case "9201":
                            {
                                MyMetadata.ShutterSpeed.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                string StringValue = BitConverter.ToInt32(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString();
                                MyMetadata.ShutterSpeed.DisplayValue = "1/" + StringValue;
                                break;
                            }

                        case "8822":
                            {
                                MyMetadata.ExposureProg.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.ExposureProg.DisplayValue = LookupEXIFValue("ExposureProg", BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString());
                                break;
                            }

                        case "8824":
                            {
                                MyMetadata.SpectralSense.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.SpectralSense.DisplayValue =Value.GetString(MyPropertyItemList[index].Value);
                                break;
                            }
                        case "8827":
                            {
                                hexVal = "";
                                MyMetadata.ISOSpeed.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                hexVal = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value).Substring(0, 2);
                                MyMetadata.ISOSpeed.DisplayValue = Convert.ToInt32(hexVal, 16).ToString();//Value.GetString(MyPropertyItemList[index].Value);
                                break;
                            }

                        case "8828":
                            {
                                MyMetadata.OECF.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.OECF.DisplayValue =Value.GetString(MyPropertyItemList[index].Value);
                                break;
                            }

                        case "9000":
                            {
                                MyMetadata.Ver.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.Ver.DisplayValue =Value.GetString(MyPropertyItemList[index].Value).Substring(1, 1)+"."+Value.GetString(MyPropertyItemList[index].Value).Substring(2, 2);
                                break;
                            }

                        case "9101":
                            {
                                MyMetadata.CompConfig.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.CompConfig.DisplayValue = LookupEXIFValue("CompConfig", BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString());
                                break;
                            }

                        case "9102":
                            {
                                MyMetadata.CompBPP.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.CompBPP.DisplayValue = BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString();
                                break;
                            }

                        case "9202":
                            {
                                hexVal = "";
                                MyMetadata.Aperture.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                hexVal = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value).Substring(0, 2);
                                hexVal = Convert.ToInt32(hexVal, 16).ToString();
                                hexVal = hexVal + "00";
                                MyMetadata.Aperture.DisplayValue = hexVal.Substring(0, 1)+"."+hexVal.Substring(1, 2);
                                break;
                            }

                        case "9203":
                            {
                                hexVal = "";
                                MyMetadata.Brightness.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                hexVal = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value).Substring(0, 2);
                                hexVal = Convert.ToInt32(hexVal, 16).ToString();
                                hexVal = hexVal + "00";
                                MyMetadata.Brightness.DisplayValue = hexVal.Substring(0, 1)+"."+hexVal.Substring(1, 2);
                                break;
                            }

                        case "9204":
                            {
                                MyMetadata.ExposureBias.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.ExposureBias.DisplayValue = BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString();
                                break;
                            }

                        case "9205":
                            {
                                hexVal = "";
                                MyMetadata.MaxAperture.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                hexVal = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value).Substring(0, 2);
                                hexVal = Convert.ToInt32(hexVal, 16).ToString();
                                hexVal = hexVal + "00";
                                MyMetadata.MaxAperture.DisplayValue = hexVal.Substring(0, 1)+"."+hexVal.Substring(1, 2);
                                break;
                            }

                        case "9206":
                            {
                                MyMetadata.SubjectDist.RawValueAsString =BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.SubjectDist.DisplayValue =Value.GetString(MyPropertyItemList[index].Value);
                                break;
                            }

                        case "9208":
                            {
                                MyMetadata.LightSource.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.LightSource.DisplayValue = LookupEXIFValue("LightSource", BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString());
                                break;
                            }

                        case "920a":
                            {
                                hexVal = "";
                                MyMetadata.FocalLength.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                hexVal = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value).Substring(0, 2);
                                hexVal = Convert.ToInt32(hexVal, 16).ToString();
                                hexVal = hexVal + "00";
                                MyMetadata.FocalLength.DisplayValue = hexVal.Substring(0, 1)+"."+hexVal.Substring(1, 2);
                                break;
                            }

                        case "a000":
                            {
                                MyMetadata.FPXVer.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.FPXVer.DisplayValue = Value.GetString(MyPropertyItemList[index].Value).Substring(1, 1)+"."+Value.GetString(MyPropertyItemList[index].Value).Substring(2, 2);
                                break;
                            }

                        case "a001":
                            {
                                MyMetadata.ColorSpace.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                if (BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString() == "1")
                                    MyMetadata.ColorSpace.DisplayValue = "RGB";
                                if (BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString() == "65535")
                                    MyMetadata.ColorSpace.DisplayValue = "Uncalibrated";
                                break;
                            }

                        case "a20e":
                            {
                                MyMetadata.FocalXRes.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.FocalXRes.DisplayValue =BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString();
                                break;
                            }

                        case "a20f":
                            {
                                MyMetadata.FocalYRes.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.FocalYRes.DisplayValue = BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString();
                                break;
                            }

                        case "a210":
                            {
                                string aa;
                                MyMetadata.FocalResUnit.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                aa = BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString(); ;
                                if (aa == "1") MyMetadata.FocalResUnit.DisplayValue = "没有单位";
                                if (aa == "2") MyMetadata.FocalResUnit.DisplayValue = "英尺";
                                if (aa == "3") MyMetadata.FocalResUnit.DisplayValue = "厘米";
                                break;
                            }

                        case "a215":
                            {
                                MyMetadata.ExposureIndex.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.ExposureIndex.DisplayValue = Value.GetString(MyPropertyItemList[index].Value);
                                break;
                            }

                        case "a217":
                            {
                                string aa;
                                aa = BitConverter.ToInt16(MyImage.GetPropertyItem(MyPropertyId).Value, 0).ToString();
                                MyMetadata.SensingMethod.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                if (aa == "2") MyMetadata.SensingMethod.DisplayValue = "1 chip color area sensor";
                                break;
                            }

                        case "a301":
                            {
                                MyMetadata.SceneType.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.SceneType.DisplayValue = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                break;
                            }

                        case "a302":
                            {
                                MyMetadata.CfaPattern.RawValueAsString = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                MyMetadata.CfaPattern.DisplayValue = BitConverter.ToString(MyImage.GetPropertyItem(MyPropertyId).Value);
                                break;
                            }



                    }
                    #endregion

                    index++;
                }
            }

            MyMetadata.XResolution.DisplayValue = MyImage.HorizontalResolution.ToString();
            MyMetadata.YResolution.DisplayValue = MyImage.VerticalResolution.ToString();
            MyMetadata.ImageHeight.DisplayValue = MyImage.Height.ToString();
            MyMetadata.ImageWidth.DisplayValue = MyImage.Width.ToString();
            MyImage.Dispose();
            return MyMetadata;
        }
        #endregion
    }

    public class ExifInfo2
    {
        public Dictionary<string, string> GetImageInfo(string path, Image image)
        {
            Dictionary<string, string> list = new Dictionary<string, string>();

            PropertyItem[] pt = image.PropertyItems;
            for (int i = 0; i < pt.Length; i++)
            {

                PropertyItem p = pt[i];
                //string str1 = System.Text.ASCIIEncoding.ASCII.GetString(pt[i].Value);
                switch (pt[i].Id)
                {  // 设备制造商 20.  
                    case 42036:
                        {
                            string str = System.Text.ASCIIEncoding.ASCII.GetString(pt[i].Value);
                            list["mount"] = str;
                        }
                        break;
                    case 36867:
                        {
                            string str = System.Text.ASCIIEncoding.ASCII.GetString(pt[i].Value);
                            list["date"] = str;
                        }
                        break;
                    case 0x010F:
                        {
                            string str = System.Text.ASCIIEncoding.ASCII.GetString(pt[i].Value);
                            list["producer"] = str;
                        }
                        break;
                    case 0x0110: // 设备型号 25.  

                        {
                            string str = GetValueOfType2(p.Value);
                            list["model"] = str;
                        }
                        break;
                    //case 0x0132: // 拍照时间 30.
                    //    {
                    //        string str = GetValueOfType2(p.Value);
                    //        list["date"] = str;
                    //    }
                    //    break;

                    case 0x829A: // .曝光时间  
                        {
                            string str = GetValueOfType5(p.Value);
                            list["exposure"] = str;
                        }
                        break;

                    case 0x8827: // ISO 40.   
                        {
                            string str = GetValueOfType3(p.Value);
                            list["ISO"] = str;
                        }
                        break;
                    case 0x010E: // 图像说明info.description
                        {
                            string str = GetValueOfType2(p.Value);
                            list["图像说明"] = str;
                        }
                        break;
                    case 0x920a: //相片的焦距

                        {
                            string str = GetValueOfType5A(p.Value) + "mm";
                            list["mm"] = str;
                        }
                        break;

                    case 0x829D: //相片的光圈值
                        {
                            string str = GetValueOfType5A(p.Value);
                            list["f"] = str;
                        }
                        break;

                    default: break;

                }

            }
            return list;
        }

        public string GetValueOfType2(byte[] b)// 对type=2 的value值进行读取
        {
            return System.Text.Encoding.ASCII.GetString(b);
        }

        private static string GetValueOfType3(byte[] b) //对type=3 的value值进行读取
        {
            if (b.Length != 2) return "unknow";
            return Convert.ToUInt16(b[1] << 8 | b[0]).ToString();
        }

        private static string GetValueOfType5(byte[] b) //对type=5 的value值进行读取
        {
            if (b.Length != 8) return "unknow";
            UInt32 fm, fz;
            fm = 0;
            fz = 0;
            fz = Convert.ToUInt32(b[7] << 24 | b[6] << 16 | b[5] << 8 | b[4]);
            fm = Convert.ToUInt32(b[3] << 24 | b[2] << 16 | b[1] << 8 | b[0]);
            return fm.ToString() + "/" + fz.ToString();

        }

        private static string GetValueOfType5A(byte[] b)//获取光圈的值
        {
            if (b.Length != 8) return "unknow";
            UInt32 fm, fz;
            fm = 0;
            fz = 0;
            fz = Convert.ToUInt32(b[7] << 24 | b[6] << 16 | b[5] << 8 | b[4]);
            fm = Convert.ToUInt32(b[3] << 24 | b[2] << 16 | b[1] << 8 | b[0]);
            double temp = (double)fm / fz;
            return (temp).ToString();
        }
    }
}
