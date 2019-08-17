using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using IniParser;
using IniParser.Model;
using Newtonsoft.Json;
using HashLib;
using Nethereum.Signer.Crypto;
using System.Text.RegularExpressions;
using Nethereum.Signer;
using Nethereum.Util;
using SHA3.Net;
using Nethereum.Web3;
using System.Buffers.Binary;
using System.Numerics;

// ReSharper disable AssignNullToNotNullAttribute
// ReSharper disable FieldCanBeMadeReadOnly.Local

namespace P2pb2b
{
    public partial class Form1 : Form
    {
        private const string HotbitApiUrl = "https://api.hotbit.io";
        private const string IdexApiUrl = "https://api.idex.market";

        private const string OldApiUrl = "https://p2pb2b.io";
        private const string NewApiUrl = "https://api.p2pb2b.io";

        private const string prefixEcsign = "\u0019Ethereum Signed Message:\n32";

        private string[] _apiKey = new string[5];
        private string[] _secretKey = new string[5];
        private static string MainName = "XLM"; // ваш токен.
        private string TokenHash = "";
        private const string ETHHash = "0x0000000000000000000000000000000000000000";
        private string[] _pairNames = { MainName + "_ETH", MainName + "_BTC", MainName + "_USD" };
        private readonly double[] _balances = { 0, 0, 0, 0 }; // ETH, BTC, USD, MainName
        private List<Order> _orders = new List<Order>();
        private double[] _lowPrice = { Double.MaxValue, Double.MaxValue, Double.MaxValue }; // ETH, BTC - наименьшие цены по ask.
        private double[] _upperPrice = { 0, 0, 0 }; // ETH, BTC - наивысшие цены по bid.
        private Thread _thread1;
        private static readonly FileIniDataParser IniParser = new FileIniDataParser();
        private IniData _iniData;
        private readonly string[] _typeOrder = {"sell", "buy", "sell" };
        private byte _timeZone;
        private bool _reportSent = false;
        private bool[] stopped = new bool[3] { false, false, false };
        private readonly string[,] _timerMain = new string[3, 2];
        private readonly string[,] _timerWait = new string[3, 2];
        private readonly string[,] _timerWaitMin = new string[3, 2];
        private readonly string[,] _timerOrders = new string[3, 2];
        private readonly string[,] _timerMainRandom = new string[3, 2];
        private readonly string[,] _countOrders = new string[3, 2];
        private readonly string[,,] _volumeLow = new string[3, 2, 2];
        private readonly string[,,] _volumeHigh = new string[3, 2, 2];
        private readonly string[,] _gap = new string[3, 2]; // зазор.
        private readonly string[,] _minPercent = new string[3, 2];
        private readonly string[,] _maxPercent = new string[3, 2];
        private readonly double[] _relyPrice = new double[3];
        private readonly double[] _minAmount = new double[2];
        private int precETH;
        private int precToken;

        private int cWallet = -1;
        private int CurWallet { get { cWallet++; cWallet %= _apiKey.Length; return cWallet; } }

        private TextBox[] Apis = new TextBox[5];
        private TextBox[] Secures = new TextBox[5];
        private Label[] Balances = new Label[5];

        private MD5 mD5 = MD5.Create();
        private Sha3Keccack keccack256 = new Sha3Keccack();

        private ASCIIEncoding ASCII = new ASCIIEncoding();
        private UTF8Encoding UTF8 = new UTF8Encoding();

        private Regex LeadingZeros = new Regex("^0+");

        private string ToMD5(string str)
        {
            byte[] data = UTF8.GetBytes(str);
            byte[] dataEncoded = mD5.ComputeHash(data);
            return BitConverter.ToString(dataEncoded).Replace("-", "");
        }

        private string SoliditySha3(params ToBeHashed[] args)
        {
            string toHash = "0x";
            foreach (ToBeHashed arg in args)
                switch (arg.T)
                {
                    case HashType.Address: toHash += arg.V.Replace("0x", ""); break;
                    case HashType.String: toHash += BitConverter.ToString(ASCII.GetBytes(arg.V)).Replace("-", ""); break;
                    case HashType.UInt256:
                        byte[] bytes = BigInteger.Parse(arg.V).ToByteArray().Reverse().ToArray();
                        string num = BitConverter.ToString(bytes).Replace("-", "");
                        toHash += new string('0', 64 - num.Length) + num;
                        break;
                }

            return keccack256.CalculateHashFromHex(toHash.ToLower());
        }

        private EthECDSASignature Ecsign(byte[] msg, string privateKey)
        {
            EthECKey prkey = new EthECKey(privateKey);
            return prkey.SignAndCalculateV(msg);
        }

        public enum HashType
        {
            String,
            Address,
            UInt256
        };

        public class ToBeHashed
        {
            public HashType T { get; private set; }
            public string V { get; private set; }

            public ToBeHashed(HashType T, string V)
            {
                this.T = T;
                this.V = V;
            }
        }

        public Form1()
        {
            InitializeComponent();

            IdexChecker.CheckedChanged += IdexChecker_CheckedChanged;
            IdexChecker.CheckedChanged += ExchangeChecker_CheckedChanged;

            mD5.Initialize();

            Apis = new TextBox[] { textBox49, textBox56, textBox58, textBox60, textBox62 };
            Secures = new TextBox[] { textBox50, textBox55, textBox57, textBox59, textBox61 };
            Balances = new Label[] { label1, label68, label69, label70, label71 };
        }

        private class Order // : IEquatable<Order>
        {
            public int ID;
            public string Pair;
            public string Type;
            public string Price;
            public string Amount;
            public string OrderHash;
            public int WalletID = -1;

            public override bool Equals(object obj)
            {
                if (obj == null || !obj.GetType().Equals(typeof(Order)))
                    return false;

                Order O = obj as Order;

                string pr1 = String.Format("{0:e}", Convert.ToDouble(Price.Replace(",", "."), CultureInfo.InvariantCulture)).Replace(",", ".");
                string pr2 = String.Format("{0:e}", Convert.ToDouble(O.Price.Replace(",", "."), CultureInfo.InvariantCulture)).Replace(",", ".");  

                string am1 = String.Format("{0:e}", Convert.ToDouble(Amount.Replace(",", "."), CultureInfo.InvariantCulture)).Replace(",", ".");
                string am2 = String.Format("{0:e}", Convert.ToDouble(O.Amount.Replace(",", "."), CultureInfo.InvariantCulture)).Replace(",", ".");

                return Pair == O.Pair && Type == O.Type && pr1 == pr2 && am1 == am2;
            }
        }

        private delegate void InvokeDelegate();

        private void InvokeMethod(string text)
        {
            try
            {
                if (richTextBox1.Lines.Length > 1200)
                {
                    var myList = richTextBox1.Lines.ToList();
                    if (myList.Count > 0)
                    {
                        myList.RemoveAt(0);
                        richTextBox1.Lines = myList.ToArray();
                        richTextBox1.Refresh();
                    }
                }
                richTextBox1.AppendText(Environment.NewLine +
                    "[" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " + text);

                if (richTextBox2.Lines.Length > 1200)
                {
                    var myList = richTextBox2.Lines.ToList();
                    if (myList.Count > 0)
                    {
                        myList.RemoveAt(0);
                        richTextBox2.Lines = myList.ToArray();
                        richTextBox2.Refresh();
                    }
                }
                richTextBox2.AppendText(Environment.NewLine +
                    "[" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " + text);

                if (richTextBox3.Lines.Length > 1200)
                {
                    var myList = richTextBox3.Lines.ToList();
                    if (myList.Count > 0)
                    {
                        myList.RemoveAt(0);
                        richTextBox3.Lines = myList.ToArray();
                        richTextBox3.Refresh();
                    }
                }
                richTextBox3.AppendText(Environment.NewLine +
                    "[" + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " + text);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void AddMessage(string text)
        {
            richTextBox1.BeginInvoke(new InvokeDelegate(() => InvokeMethod(text)));
        }

        private static string GetRandomNumber(double minimum, double maximum, bool isPrice)
        {
            var random = new Random();
            return (random.NextDouble()*(maximum - minimum) + minimum).ToString(isPrice ? "0.##########" : "0.##");
        }

        private void SetMySettings()
        {
            try
            {
                // ETH

                _iniData["Settings"]["TimerMain"] = textBox8.Text + ";" + textBox15.Text;
                _iniData["Settings"]["TimerWait"] = textBox9.Text + ";" + textBox13.Text;
                _iniData["Settings"]["TimerWaitMin"] = textBox51.Text + ";" + textBox52.Text;
                _iniData["Settings"]["TimerOrders"] = textBox10.Text + ";" + textBox12.Text;
                _iniData["Settings"]["TimerMainRandom"] = textBox11.Text + ";" + textBox14.Text;
                _iniData["Settings"]["CountOrders"] = textBox5.Text + ";" + textBox22.Text;
                _iniData["Settings"]["VolumeLow"] = textBox6.Text + "|" + textBox41.Text + ";" + textBox21.Text + "|" + textBox43.Text;
                _iniData["Settings"]["VolumeHigh"] = textBox7.Text + "|" + textBox42.Text + ";" + textBox20.Text + "|" + textBox44.Text;

                _iniData["Settings"]["Gap"] = (checkBox1.Checked ? "1" : "0") + ";" + textBox16.Text;
                _iniData["Settings"]["MinPercent"] = textBox18.Text + ";" + textBox24.Text;
                _iniData["Settings"]["MaxPercent"] = textBox19.Text + ";" + textBox23.Text;

                _timerMain[0, 0] = textBox8.Text;
                _timerMain[0, 1] = textBox15.Text;
                _timerWait[0, 0] = textBox9.Text;
                _timerWait[0, 1] = textBox13.Text;
                _timerWaitMin[0, 0] = textBox51.Text;
                _timerWaitMin[0, 1] = textBox52.Text;
                _timerOrders[0, 0] = textBox10.Text;
                _timerOrders[0, 1] = textBox12.Text;
                _timerMainRandom[0, 0] = textBox11.Text;
                _timerMainRandom[0, 1] = textBox14.Text;
                _countOrders[0, 0] = textBox5.Text;
                _countOrders[0, 1] = textBox22.Text;
                _volumeLow[0, 0, 0] = textBox6.Text;
                _volumeLow[0, 1, 0] = textBox21.Text;
                _volumeLow[0, 0, 1] = textBox41.Text;
                _volumeLow[0, 1, 1] = textBox43.Text;
                _volumeHigh[0, 0, 0] = textBox7.Text;
                _volumeHigh[0, 1, 0] = textBox20.Text;
                _volumeHigh[0, 0, 1] = textBox42.Text;
                _volumeHigh[0, 1, 1] = textBox44.Text;

                _gap[0, 0] = checkBox1.Checked ? "1" : "0";
                _gap[0, 1] = textBox16.Text;
                _minPercent[0, 0] = textBox18.Text;
                _minPercent[0, 1] = textBox24.Text;
                _maxPercent[0, 0] = textBox19.Text;
                _maxPercent[0, 1] = textBox23.Text;

                // BTC

                _iniData["Settings"]["TimerMainBtc"] = textBox30.Text + ";" + textBox37.Text;
                _iniData["Settings"]["TimerWaitBtc"] = textBox28.Text + ";" + textBox35.Text;
                _iniData["Settings"]["TimerWaitMinBtc"] = textBox53.Text + ";" + textBox54.Text;
                _iniData["Settings"]["TimerOrdersBtc"] = textBox27.Text + ";" + textBox34.Text;
                _iniData["Settings"]["TimerMainRandomBtc"] = textBox29.Text + ";" + textBox36.Text;
                _iniData["Settings"]["CountOrdersBtc"] = textBox26.Text + ";" + textBox33.Text;
                _iniData["Settings"]["VolumeLowBtc"] = textBox4.Text + "|" + textBox45.Text + ";" + textBox32.Text + "|" + textBox47.Text;
                _iniData["Settings"]["VolumeHighBtc"] = textBox3.Text + "|" + textBox46.Text + ";" + textBox31.Text + "|" + textBox48.Text;

                _iniData["Settings"]["GapBtc"] = (checkBox2.Checked ? "1" : "0") + ";" + textBox17.Text;
                _iniData["Settings"]["MinPercentBtc"] = textBox38.Text + ";" + textBox40.Text;
                _iniData["Settings"]["MaxPercentBtc"] = textBox25.Text + ";" + textBox39.Text;

                _timerMain[1, 0] = textBox30.Text;
                _timerMain[1, 1] = textBox37.Text;
                _timerWait[1, 0] = textBox28.Text;
                _timerWait[1, 1] = textBox35.Text;
                _timerWaitMin[1, 0] = textBox53.Text;
                _timerWaitMin[1, 1] = textBox54.Text;
                _timerOrders[1, 0] = textBox27.Text;
                _timerOrders[1, 1] = textBox34.Text;
                _timerMainRandom[1, 0] = textBox29.Text;
                _timerMainRandom[1, 1] = textBox36.Text;
                _countOrders[1, 0] = textBox26.Text;
                _countOrders[1, 1] = textBox33.Text;
                _volumeLow[1, 0, 0] = textBox4.Text;
                _volumeLow[1, 1, 0] = textBox32.Text;
                _volumeLow[1, 0, 1] = textBox45.Text;
                _volumeLow[1, 1, 1] = textBox47.Text;
                _volumeHigh[1, 0, 0] = textBox3.Text;
                _volumeHigh[1, 1, 0] = textBox31.Text;
                _volumeHigh[1, 0, 1] = textBox46.Text;
                _volumeHigh[1, 1, 1] = textBox48.Text;

                _gap[1, 0] = checkBox2.Checked ? "1" : "0";
                _gap[1, 1] = textBox17.Text;
                _minPercent[1, 0] = textBox38.Text;
                _minPercent[1, 1] = textBox40.Text;
                _maxPercent[1, 0] = textBox25.Text;
                _maxPercent[1, 1] = textBox39.Text;

                //USD

                _iniData["Settings"]["TimerMainUsd"] = textBox75.Text + ";" + textBox87.Text;
                _iniData["Settings"]["TimerWaitUsd"] = textBox73.Text + ";" + textBox85.Text;
                _iniData["Settings"]["TimerWaitMinUsd"] = textBox64.Text + ";" + textBox76.Text;
                _iniData["Settings"]["TimerOrdersUsd"] = textBox72.Text + ";" + textBox84.Text;
                _iniData["Settings"]["TimerMainRandomUsd"] = textBox74.Text + ";" + textBox86.Text;
                _iniData["Settings"]["CountOrdersUsd"] = textBox71.Text + ";" + textBox83.Text;
                _iniData["Settings"]["VolumeLowUsd"] = textBox70.Text + "|" + textBox66.Text + ";" + textBox82.Text + "|" + textBox78.Text;
                _iniData["Settings"]["VolumeHighUsd"] = textBox69.Text + "|" + textBox65.Text + ";" + textBox81.Text + "|" + textBox77.Text;

                _iniData["Settings"]["GapUsd"] = (checkBox3.Checked ? "1" : "0") + ";" + textBox63.Text;
                _iniData["Settings"]["MinPercentUsd"] = textBox68.Text + ";" + textBox80.Text;
                _iniData["Settings"]["MaxPercentUsd"] = textBox67.Text + ";" + textBox79.Text;

                _timerMain[2, 0] = textBox75.Text;
                _timerMain[2, 1] = textBox87.Text;
                _timerWait[2, 0] = textBox73.Text;
                _timerWait[2, 1] = textBox85.Text;
                _timerWaitMin[2, 0] = textBox64.Text;
                _timerWaitMin[2, 1] = textBox76.Text;
                _timerOrders[2, 0] = textBox72.Text;
                _timerOrders[2, 1] = textBox84.Text;
                _timerMainRandom[2, 0] = textBox74.Text;
                _timerMainRandom[2, 1] = textBox86.Text;
                _countOrders[2, 0] = textBox71.Text;
                _countOrders[2, 1] = textBox83.Text;
                _volumeLow[2, 0, 0] = textBox70.Text;
                _volumeLow[2, 1, 0] = textBox82.Text;
                _volumeLow[2, 0, 1] = textBox66.Text;
                _volumeLow[2, 1, 1] = textBox78.Text;
                _volumeHigh[2, 0, 0] = textBox69.Text;
                _volumeHigh[2, 1, 0] = textBox81.Text;
                _volumeHigh[2, 0, 1] = textBox65.Text;
                _volumeHigh[2, 1, 1] = textBox77.Text;

                _gap[2, 0] = checkBox3.Checked ? "1" : "0";
                _gap[2, 1] = textBox63.Text;
                _minPercent[2, 0] = textBox68.Text;
                _minPercent[2, 1] = textBox80.Text;
                _maxPercent[2, 0] = textBox67.Text;
                _maxPercent[2, 1] = textBox79.Text;


                IniParser.WriteFile("config.ini", _iniData);
            }
            catch (Exception ex)
            {
                AddMessage("SetMySettings: " + ex.Message);
            }
        }

        private void LoadSettings()
        {
            try
            {
                for (int i = 0; i < _apiKey.Length; i++)
                {
                    if (_iniData["Account"]["ApiKey" + i] != null)
                    {
                        _apiKey[i] = _iniData["Account"]["ApiKey" + i];
                        Apis[i].Text = _apiKey[i];
                    }
                    else
                        _iniData["Account"]["ApiKey" + i] = _apiKey[i];

                    if (_iniData["Account"]["SecretKey" + i] != null)
                    {
                        _secretKey[i] = _iniData["Account"]["SecretKey" + i];
                        Secures[i].Text = _secretKey[i];
                    }
                    else
                        _iniData["Account"]["SecretKey" + i] = _secretKey[i];
                }

                IniParser.WriteFile("config.ini", _iniData);

                // ETH

                if (_iniData["Settings"]["TimerMain"] != null)
                {
                    var items = _iniData["Settings"]["TimerMain"].Split(';');
                    textBox8.Text = items[0];
                    textBox15.Text = items[1];
                }
                if (_iniData["Settings"]["TimerWait"] != null)
                {
                    var items = _iniData["Settings"]["TimerWait"].Split(';');
                    textBox9.Text = items[0];
                    textBox13.Text = items[1];
                }
                if (_iniData["Settings"]["TimerWaitMin"] != null)
                {
                    var items = _iniData["Settings"]["TimerWaitMin"].Split(';');
                    textBox51.Text = items[0];
                    textBox52.Text = items[1];
                }
                if (_iniData["Settings"]["TimerOrders"] != null)
                {
                    var items = _iniData["Settings"]["TimerOrders"].Split(';');
                    textBox10.Text = items[0];
                    textBox12.Text = items[1];
                }
                if (_iniData["Settings"]["TimerMainRandom"] != null)
                {
                    var items = _iniData["Settings"]["TimerMainRandom"].Split(';');
                    textBox11.Text = items[0];
                    textBox14.Text = items[1];
                }
                if (_iniData["Settings"]["CountOrders"] != null)
                {
                    var items = _iniData["Settings"]["CountOrders"].Split(';');
                    textBox5.Text = items[0];
                    textBox22.Text = items[1];
                }
                if (_iniData["Settings"]["VolumeLow"] != null)
                {
                    var items = _iniData["Settings"]["VolumeLow"].Split(';');
                    textBox6.Text = items[0].Split('|')[0];
                    textBox41.Text = items[0].Split('|')[1];
                    textBox21.Text = items[1].Split('|')[0];
                    textBox43.Text = items[1].Split('|')[1];
                }
                if (_iniData["Settings"]["VolumeHigh"] != null)
                {
                    var items = _iniData["Settings"]["VolumeHigh"].Split(';');
                    textBox7.Text = items[0].Split('|')[0];
                    textBox42.Text = items[0].Split('|')[1];
                    textBox20.Text = items[1].Split('|')[0];
                    textBox44.Text = items[1].Split('|')[1];
                }

                if (_iniData["Settings"]["Gap"] != null)
                {
                    var items = _iniData["Settings"]["Gap"].Split(';');
                    checkBox1.Checked = items[0] == "1";
                    textBox16.Text = items[1];
                }
                if (_iniData["Settings"]["MinPercent"] != null)
                {
                    var items = _iniData["Settings"]["MinPercent"].Split(';');
                    textBox18.Text = items[0];
                    textBox24.Text = items[1];
                }
                if (_iniData["Settings"]["MaxPercent"] != null)
                {
                    var items = _iniData["Settings"]["MaxPercent"].Split(';');
                    textBox19.Text = items[0];
                    textBox23.Text = items[1];
                }

                // BTC

                if (_iniData["Settings"]["TimerMainBtc"] != null)
                {
                    var items = _iniData["Settings"]["TimerMainBtc"].Split(';');
                    textBox30.Text = items[0];
                    textBox37.Text = items[1];
                }
                if (_iniData["Settings"]["TimerWaitBtc"] != null)
                {
                    var items = _iniData["Settings"]["TimerWaitBtc"].Split(';');
                    textBox28.Text = items[0];
                    textBox35.Text = items[1];
                }
                if (_iniData["Settings"]["TimerWaitMinBtc"] != null)
                {
                    var items = _iniData["Settings"]["TimerWaitMinBtc"].Split(';');
                    textBox53.Text = items[0];
                    textBox54.Text = items[1];
                }
                if (_iniData["Settings"]["TimerOrdersBtc"] != null)
                {
                    var items = _iniData["Settings"]["TimerOrdersBtc"].Split(';');
                    textBox27.Text = items[0];
                    textBox34.Text = items[1];
                }
                if (_iniData["Settings"]["TimerMainRandomBtc"] != null)
                {
                    var items = _iniData["Settings"]["TimerMainRandomBtc"].Split(';');
                    textBox29.Text = items[0];
                    textBox36.Text = items[1];
                }
                if (_iniData["Settings"]["CountOrdersBtc"] != null)
                {
                    var items = _iniData["Settings"]["CountOrdersBtc"].Split(';');
                    textBox26.Text = items[0];
                    textBox33.Text = items[1];
                }
                if (_iniData["Settings"]["VolumeLowBtc"] != null)
                {
                    var items = _iniData["Settings"]["VolumeLowBtc"].Split(';');
                    textBox4.Text = items[0].Split('|')[0];
                    textBox45.Text = items[0].Split('|')[1];
                    textBox32.Text = items[1].Split('|')[0];
                    textBox47.Text = items[1].Split('|')[1];
                }
                if (_iniData["Settings"]["VolumeHighBtc"] != null)
                {
                    var items = _iniData["Settings"]["VolumeHighBtc"].Split(';');
                    textBox3.Text = items[0].Split('|')[0];
                    textBox46.Text = items[0].Split('|')[1];
                    textBox31.Text = items[1].Split('|')[0];
                    textBox48.Text = items[1].Split('|')[1];
                }

                if (_iniData["Settings"]["GapBtc"] != null)
                {
                    var items = _iniData["Settings"]["GapBtc"].Split(';');
                    checkBox2.Checked = items[0] == "1";
                    textBox17.Text = items[1];
                }
                if (_iniData["Settings"]["MinPercentBtc"] != null)
                {
                    var items = _iniData["Settings"]["MinPercentBtc"].Split(';');
                    textBox38.Text = items[0];
                    textBox40.Text = items[1];
                }
                if (_iniData["Settings"]["MaxPercentBtc"] != null)
                {
                    var items = _iniData["Settings"]["MaxPercentBtc"].Split(';');
                    textBox25.Text = items[0];
                    textBox39.Text = items[1];
                }

                //USD

                if (_iniData["Settings"]["TimerMainUsd"] != null)
                {
                    var items = _iniData["Settings"]["TimerMainUsd"].Split(';');
                    textBox75.Text = items[0];
                    textBox87.Text = items[1];
                }
                if (_iniData["Settings"]["TimerWaitUsd"] != null)
                {
                    var items = _iniData["Settings"]["TimerWaitUsd"].Split(';');
                    textBox73.Text = items[0];
                    textBox85.Text = items[1];
                }
                if (_iniData["Settings"]["TimerWaitMinUsd"] != null)
                {
                    var items = _iniData["Settings"]["TimerWaitMinUsd"].Split(';');
                    textBox64.Text = items[0];
                    textBox76.Text = items[1];
                }
                if (_iniData["Settings"]["TimerOrdersUsd"] != null)
                {
                    var items = _iniData["Settings"]["TimerOrdersUsd"].Split(';');
                    textBox72.Text = items[0];
                    textBox84.Text = items[1];
                }
                if (_iniData["Settings"]["TimerMainRandomUsd"] != null)
                {
                    var items = _iniData["Settings"]["TimerMainRandomUsd"].Split(';');
                    textBox74.Text = items[0];
                    textBox86.Text = items[1];
                }
                if (_iniData["Settings"]["CountOrdersUsd"] != null)
                {
                    var items = _iniData["Settings"]["CountOrdersUsd"].Split(';');
                    textBox71.Text = items[0];
                    textBox83.Text = items[1];
                }
                if (_iniData["Settings"]["VolumeLowUsd"] != null)
                {
                    var items = _iniData["Settings"]["VolumeLowUsd"].Split(';');
                    textBox70.Text = items[0].Split('|')[0];
                    textBox66.Text = items[0].Split('|')[1];
                    textBox82.Text = items[1].Split('|')[0];
                    textBox87.Text = items[1].Split('|')[1];
                }
                if (_iniData["Settings"]["VolumeHighUsd"] != null)
                {
                    var items = _iniData["Settings"]["VolumeHighUsd"].Split(';');
                    textBox69.Text = items[0].Split('|')[0];
                    textBox65.Text = items[0].Split('|')[1];
                    textBox81.Text = items[1].Split('|')[0];
                    textBox77.Text = items[1].Split('|')[1];
                }

                if (_iniData["Settings"]["GapUsd"] != null)
                {
                    var items = _iniData["Settings"]["GapUsd"].Split(';');
                    checkBox3.Checked = items[0] == "1";
                    textBox63.Text = items[1];
                }
                if (_iniData["Settings"]["MinPercentUsd"] != null)
                {
                    var items = _iniData["Settings"]["MinPercentUsd"].Split(';');
                    textBox68.Text = items[0];
                    textBox80.Text = items[1];
                }
                if (_iniData["Settings"]["MaxPercentUsd"] != null)
                {
                    var items = _iniData["Settings"]["MaxPercentUsd"].Split(';');
                    textBox67.Text = items[0];
                    textBox79.Text = items[1];
                }
            }
            catch (Exception ex)
            {
                AddMessage("LoadSettings: " + ex.Message);
            }
        }

        private void FillTables()
        {
            try
            {
                dataGridView1.Rows.Clear();
                dataGridView2.Rows.Clear();
                dataGridView3.Rows.Clear();
                dataGridView4.Rows.Clear();
                dataGridView5.Rows.Clear();
                dataGridView6.Rows.Clear();

                foreach (var t in _orders)
                {
                    if (t.Pair == MainName + "_ETH")
                    {
                        if (t.Type == "sell")
                            dataGridView1.Rows.Add(t.Pair, t.Price, t.Amount);
                        else
                            dataGridView2.Rows.Add(t.Pair, t.Price, t.Amount);
                    }
                    else if (t.Pair == MainName + "_BTC")
                    {
                        if (t.Type == "sell")
                            dataGridView3.Rows.Add(t.Pair, t.Price, t.Amount);
                        else
                            dataGridView4.Rows.Add(t.Pair, t.Price, t.Amount);
                    }
                    else if (t.Pair == MainName + "_USD")
                    {
                        if (t.Type == "sell")
                            dataGridView5.Rows.Add(t.Pair, t.Price, t.Amount);
                        else
                            dataGridView6.Rows.Add(t.Pair, t.Price, t.Amount);
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("FillTables: " + ex.Message);
            }
        }

        private void ApiGetBalances()
        {
            if (P2P2B2BChecker.Checked)
                ApiGetBalancesP2P2B2B();
            else if (HotbitChecker.Checked)
                ApiGetBalancesHotbit();
            else if (IdexChecker.Checked)
                ApiGetBalancesIdex();
        }

        private void ApiGetBalancesIdex()
        {
            const string GetBalancePath = "/returnBalances";
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    string requestText = "{ \"address\" : \"" + Apis[i].Text + "\" }";
                    byte[] data = UTF8.GetBytes(requestText);

                    HttpWebRequest request = WebRequest.Create(IdexApiUrl + GetBalancePath) as HttpWebRequest;
                    request.Headers.Add("Payload", Convert.ToBase64String(data));
                    request.Method = WebRequestMethods.Http.Post;
                    request.ContentType = "application/json";
                    request.ContentLength = data.Length;

                    request.GetRequestStream().Write(data, 0, data.Length);

                    dynamic m;
                    using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                    using (StreamReader str = new StreamReader(response.GetResponseStream()))
                        m = JsonConvert.DeserializeObject(str.ReadToEnd());

                    AddMessage("ApiGetBalances: " + (m.error == null ? "success" : "failure") + "(wallet " + (i + 1) + ")");

                    _balances[0] = Convert.ToDouble(m["ETH"]);
                    _balances[1] = 0;
                    _balances[2] = 0;
                    _balances[3] = Convert.ToDouble(m[MainName]);

                    Balances[i].Text = "ETH " + (m["ETH"] != null ? m["ETH"] : "0")  + Environment.NewLine + MainName + " " + (m[MainName] != null ? m[MainName] : "0");
                }
                catch (WebException ex)
                {
                    try
                    {
                        string h;
                        using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                            h = sr.ReadToEnd();
                        AddMessage("ApiGetBalances: " + h);
                        Balances[i].Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + _balances[3].ToString("0.########") + @" " + MainName;
                    }
                    catch
                    {
                        MessageBox.Show("No Internet connection");
                        this.Close();
                    }
                }
                catch (Exception ex)
                {
                    AddMessage("ApiGetBalances: " + ex.Message);
                    Balances[i].Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + _balances[3].ToString("0.########") + @" " + MainName;
                }
            }
        }

        private void ApiGetBalancesP2P2B2B() // API
        {
            try
            {
                var unixTimestamp = Convert.ToInt64((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds).ToString();
                const string query = "/api/v1/account/balances";

                var request = (HttpWebRequest)WebRequest.Create(OldApiUrl + query);
                var jsonData = "{\"request\":\"" + query + "\",\"nonce\":" + unixTimestamp + "}";
                var data = Encoding.ASCII.GetBytes(jsonData);

                request.Headers.Add("X-TXC-APIKEY", _apiKey[0]);
                request.Headers.Add("X-TXC-SIGNATURE", GetSign512(_secretKey[0], Convert.ToBase64String(data), false));
                request.Headers.Add("X-TXC-PAYLOAD", Convert.ToBase64String(data));

                request.Method = "POST";
                request.ContentType = "application/json";
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                var response = (HttpWebResponse)request.GetResponse();
                var resStream = new StreamReader(response.GetResponseStream()).ReadToEnd();
                dynamic m = JsonConvert.DeserializeObject(resStream);
                AddMessage("ApiGetBalances: " + m.success.Value);
                Thread.Sleep(200);
                foreach (var pair in m.result)
                {
                    string currency = pair.Name;
                    double balance = Convert.ToDouble(pair.Value.available);

                    if(currency == "ETH")
                        _balances[0] = balance;
                    else if(currency == "BTC")
                        _balances[1] = balance;
                    else if(currency == "USD")
                        _balances[2] = balance;
                    else if(currency == MainName)
                        _balances[3] = balance;
                }
                label1.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + 
                              _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine + 
                              _balances[2].ToString("0.########") + @" USD" + Environment.NewLine +
                              _balances[3].ToString("0.########") + @" " + MainName;
                label2.Text = label1.Text;
                label96.Text = label1.Text;
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetBalances: " + h);

                    for (int i = 0; i < 4; i++)
                        _balances[i] = 0;

                    label1.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine +
                                  _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine +
                                  _balances[2].ToString("0.########") + @" USD" + Environment.NewLine +
                                  _balances[3].ToString("0.########") + @" " + MainName;
                    label2.Text = label1.Text;
                    label96.Text = label1.Text;
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiGetBalances: " + ex.Message);

                for (int i = 0; i < 4; i++)
                    _balances[i] = 0;

                label1.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine +
                              _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine +
                              _balances[2].ToString("0.########") + @" USD" + Environment.NewLine +
                              _balances[3].ToString("0.########") + @" " + MainName;
                label2.Text = label1.Text;
                label96.Text = label1.Text;
            }
        }

        private void ApiGetBalancesHotbit() //API
        {
            try
            {
                const string GetBalancePath = "/api/v1/balance.query";

                string preRequestText = "api_key=" + _apiKey + "&assets=[\"ETH\",\"BTC\",\"" + MainName + "\"]&secret_key=" + _secretKey;
                string requestBody = "api_key=" + _apiKey + "&assets=[\"ETH\",\"BTC\",\"" + MainName + "\"]&sign=" + ToMD5(preRequestText).ToUpper();

                WebClient WC = new WebClient();
                dynamic m = JsonConvert.DeserializeObject(WC.DownloadString(HotbitApiUrl + GetBalancePath + "?" + requestBody));

                AddMessage("ApiGetBalances: " + (m.error == null ? "success" : "failure"));
                foreach (var pair in m.result)
                {
                    string currency = pair.Name;
                    double balance = Convert.ToDouble(pair.Value.available);

                    if (currency == "ETH")
                        _balances[0] = balance;
                    else if (currency == "BTC")
                        _balances[1] = balance;
                    else if (currency == MainName)
                        _balances[3] = balance;
                }
                label1.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine + _balances[3].ToString("0.########") + @" " + MainName;
                label2.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine + _balances[3].ToString("0.########") + @" " + MainName;

            } catch (WebException ex) {
                try {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetBalances: " + h);

                    for (int i = 0; i < 4; i++)
                        _balances[i] = 0;

                    label1.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine + _balances[3].ToString("0.########") + @" " + MainName;
                    label2.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine + _balances[3].ToString("0.########") + @" " + MainName;
                } catch {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                }
            } catch(Exception ex) {
                AddMessage("ApiGetBalances: " + ex.Message);

                for (int i = 0; i < 4; i++)
                    _balances[i] = 0;

                label1.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine + _balances[3].ToString("0.########") + @" " + MainName;
                label2.Text = _balances[0].ToString("0.########") + @" ETH" + Environment.NewLine + _balances[1].ToString("0.########") + @" BTC" + Environment.NewLine + _balances[3].ToString("0.########") + @" " + MainName;
            }
        }

        private void ApiGetMinAmount()
        {
            if (P2P2B2BChecker.Checked)
                ApiGetMinAmountP2P2B2B();
            else if (HotbitChecker.Checked)
                ApiGetMinAmountHotbit();
            else if (IdexChecker.Checked)
                ApiGetMinAmountIdex();
        }

        private void ApiGetMinAmountHotbit()
        {
            try
            {
                const string GetBalancePath = "/api/v1/market.list";

                WebClient WC = new WebClient();
                dynamic m = JsonConvert.DeserializeObject(WC.DownloadString(HotbitApiUrl + GetBalancePath));

                if (m.error != null)
                    AddMessage("ApiGetMinAmount: Failed: " + m.error);
                else
                    for (int i = 0; i < _pairNames.Length - 1; i++)
                        foreach (var pair in m.result)
                            if (pair.name == _pairNames[i].Replace("_", ""))
                                _minAmount[i] = Convert.ToDouble(pair.min_amount, CultureInfo.InvariantCulture);
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetMinAmount: " + h);
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                }
                _minAmount[0] = 0;
                _minAmount[1] = 0;
            }
            catch (Exception ex)
            {
                AddMessage("ApiGetMinAmount: " + ex.Message);
                _minAmount[0] = 0;
                _minAmount[1] = 0;
            }
        }

        private void ApiGetMinAmountIdex() { }
        private void ApiGetMinAmountP2P2B2B() { }

        private void ApiGetAllOrders()
        {
            if (P2P2B2BChecker.Checked)
                ApiGetAllOrdersP2P2B2B();
            else if (HotbitChecker.Checked)
                ApiGetAllOrdersHotbit();
            else if (IdexChecker.Checked)
                ApiGetAllOrdersIdex();
        }

        private void ApiGetAllOrdersIdex()
        {
            try
            {
                _orders.Clear();
                for (int i = 0; i < _pairNames.Length; i++)
                {
                    _lowPrice[i] = Double.MaxValue;
                    _upperPrice[i] = 0;
                }

                const string GetOrdersPath = "/returnOrderBook";
                
                for (int i = 0; i < _pairNames.Length - 2; i++)
                {
                    string[] pair = _pairNames[i].Split('_');
                    string requestText = "{ \"market\" : \"" + pair[1] + "_" + pair[0] + "\", \"count\" : 100 }";
                    byte[] data = UTF8.GetBytes(requestText);

                    HttpWebRequest request = WebRequest.Create(IdexApiUrl + GetOrdersPath) as HttpWebRequest;
                    request.Method = WebRequestMethods.Http.Post;
                    request.ContentType = "application/json";
                    request.ContentLength = data.Length;

                    request.GetRequestStream().Write(data, 0, data.Length);

                    dynamic m;
                    using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                    using (StreamReader str = new StreamReader(response.GetResponseStream()))
                        m = JsonConvert.DeserializeObject(str.ReadToEnd());

                    if (m.error != null)
                        continue;

                    foreach (var ask in m.asks)
                    {
                        double price = Convert.ToDouble(ask.price, CultureInfo.InvariantCulture);
                        _orders.Add(new Order
                        {
                            Pair = _pairNames[i],
                            Type = "sell",
                            Price = price.ToString(),
                            Amount = ask.amount
                        });


                        if (_lowPrice[i] > price)
                            _lowPrice[i] = price;
                    }

                    foreach (var ask in m.bids)
                    {
                        double price = Convert.ToDouble(ask.price, CultureInfo.InvariantCulture);
                        _orders.Add(new Order
                        {
                            Pair = _pairNames[i],
                            Type = "buy",
                            Price = price.ToString(),
                            Amount = ask.amount
                        });

                        if (_upperPrice[i] < price)
                            _upperPrice[i] = price;
                    }
                }
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetAllOrders: " + h);
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiGetAllOrders: " + ex.Message);
            }
        }

        private void ApiGetAllOrdersHotbit() //API
        {
            try
            {
                _orders.Clear();
                for (int i = 0; i < _pairNames.Length; i++)
                {
                    _lowPrice[i] = Double.MaxValue;
                    _upperPrice[i] = 0;
                }

                const string GetOrdersPath = "/api/v1/order.depth";
                
                for(int i = 0; i < _pairNames.Length - 1; i++)
                {
                    string requestText = "market=" + _pairNames[i].Replace("_", "/") + "&limit=100&interval=0.0000000001";
                    HttpWebRequest request = WebRequest.Create(HotbitApiUrl + GetOrdersPath + "?" + requestText) as HttpWebRequest;
                    request.Method = WebRequestMethods.Http.Get;
                    request.ContentType = "application/x-www-form-urlencoded";

                    dynamic m;
                    using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                    using (StreamReader str = new StreamReader(response.GetResponseStream()))
                        m = JsonConvert.DeserializeObject(str.ReadToEnd());

                    if (m.error != null)
                        continue;

                    foreach(var ask in m.result.asks)
                    {
                        double price = Convert.ToDouble(ask[0], CultureInfo.InvariantCulture);
                        _orders.Add(new Order
                        {
                            Pair = _pairNames[i],
                            Type = "sell",
                            Price = price.ToString(),
                            Amount = ask[1]
                        });

                        
                        if (_lowPrice[i] > price)
                            _lowPrice[i] = price;
                    }

                    foreach (var ask in m.result.bids)
                    {
                        double price = Convert.ToDouble(ask[0], CultureInfo.InvariantCulture);
                        _orders.Add(new Order
                        {
                            Pair = _pairNames[i],
                            Type = "buy",
                            Price = price.ToString(),
                            Amount = ask[1]
                        });

                        if (_upperPrice[i] < price)
                            _upperPrice[i] = price;
                    }
                }
            }
            catch (WebException ex)
            {
                try {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetAllOrders: " + h);
                } catch {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiGetAllOrders: " + ex.Message);
            }
        }

        private void ApiGetAllOrdersP2P2B2B() //API
        {
            try
            {
                _orders.Clear();
                for(int i = 0; i < _pairNames.Length; i++)
                {
                    _lowPrice[i] = Double.MaxValue;
                    _upperPrice[i] = 0;
                }

                for (int i = 0; i < _pairNames.Length; i++)
                {
                    var query = "/api/v1/public/depth/result?market=" + _pairNames[i] + "&limit=100";
                    var request = (HttpWebRequest)WebRequest.Create(NewApiUrl + query);
                    request.Method = WebRequestMethods.Http.Get;
                    request.ContentType = "application/x-www-form-urlencoded";

                    var response = (HttpWebResponse)request.GetResponse();
                    var resStream = new StreamReader(response.GetResponseStream()).ReadToEnd();
                    dynamic m = JsonConvert.DeserializeObject(resStream);
                    Thread.Sleep(1000);

                    foreach (var ask in m.result.asks)
                    {
                        _orders.Add(new Order
                        {
                            Pair = _pairNames[i],
                            Type = "sell",
                            Price = ask[0],
                            Amount = ask[1]
                        });
                        if (_lowPrice[i] > Convert.ToDouble(ask[0]))
                            _lowPrice[i] = Convert.ToDouble(ask[0]);
                    }

                    foreach (var ask in m.result.bids)
                    {
                        _orders.Add(new Order
                        {
                            Pair = _pairNames[i],
                            Type = "buy",
                            Price = ask[0],
                            Amount = ask[1]
                        });
                        if (_upperPrice[i] < Convert.ToDouble(ask[0]))
                            _upperPrice[i] = Convert.ToDouble(ask[0]);
                    }
                }
            }
            catch (WebException ex)
            {
                try {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetAllOrders: " + h);
                } catch {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiGetAllOrders: " + ex.Message);
            }            
        }

        private void ApiGetTokenInfo()
        {
            string GetTokenInfoPath = "/returnCurrencies";
            try
            {
                HttpWebRequest request = HttpWebRequest.CreateHttp(IdexApiUrl + GetTokenInfoPath);
                request.Method = WebRequestMethods.Http.Post;
                request.ContentType = "application/json";

                dynamic m;
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                using (StreamReader str = new StreamReader(response.GetResponseStream()))
                    m = JsonConvert.DeserializeObject(str.ReadToEnd());

                if (m.error != null)
                    throw new Exception(m.error);

                TokenHash = m[MainName].address;
                _minAmount[0] = Math.Pow(10, -Math.Min(Convert.ToInt32(m["ETH"].decimals), Convert.ToInt32(m[MainName].decimals)));
                precETH = Convert.ToInt32(m["ETH"].decimals);
                precToken = Convert.ToInt32(m[MainName].decimals);
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetTokenInfo: " + h);
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiGetTokenInfo: " + ex.Message);
            }
        }

        private string ApiGetContractAddressIdex()
        {
            string GetContractAddressPath = "/returnContractAddress";
            try
            {
                HttpWebRequest request = HttpWebRequest.CreateHttp(IdexApiUrl + GetContractAddressPath);
                request.Method = WebRequestMethods.Http.Post;
                request.ContentType = "application/json";

                dynamic m;
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                using (StreamReader str = new StreamReader(response.GetResponseStream()))
                    m = JsonConvert.DeserializeObject(str.ReadToEnd());

                if (m.error != null)
                    throw new Exception(m.error);

                return m.address;
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetContractAddress: " + h);
                    return null;
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                    return null;
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiGetContractAddress: " + ex.Message);
                return null;
            }
        }

        private string ApiGetNextNonceIdex(int walletNum)
        {
            string GetNextNoncePath = "/returnNextNonce";
            try
            {
                string requestBody = "{ \"address\" : \"" + _apiKey[walletNum] + "\" }";
                byte[] data = ASCII.GetBytes(requestBody);

                HttpWebRequest request = HttpWebRequest.CreateHttp(IdexApiUrl + GetNextNoncePath);
                request.Method = WebRequestMethods.Http.Post;
                request.ContentType = "application/json";
                request.ContentLength = data.Length;

                request.GetRequestStream().Write(data, 0, data.Length);

                dynamic m;
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                using (StreamReader str = new StreamReader(response.GetResponseStream()))
                    m = JsonConvert.DeserializeObject(str.ReadToEnd());

                if (m.error != null)
                    throw new Exception(m.error);

                return m.nonce;
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiGetNextNonce: " + h);
                    return null;
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                    return null;
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiGetNextNonce: " + ex.Message);
                return null;
            }
        }

        private Order ApiCreateOrder(int pairNum, string amount, string price, string type, int walletNum = -1)
        {
            if (P2P2B2BChecker.Checked)
                return ApiCreateOrderP2P2B2B(_pairNames[pairNum], amount, price, type);
            else if (HotbitChecker.Checked)
                return ApiCreateOrderHotbit(pairNum, amount, price, type);
            else if (IdexChecker.Checked)
                return ApiCreateOrderIdex(pairNum, amount, price, type, walletNum);
            return null;
        }

        private Order ApiCreateOrderIdex(int pairNum, string _amount, string _price, string type, int walletNum = -1)
        {
            return null;
        }

        private Order ApiCreateOrderHotbit(int pairNum, string _amount, string _price, string type)
        {
            try
            {
                if (_minAmount[pairNum] == 0)
                    ApiGetMinAmount();

                string price = String.Format("{0:e}", Convert.ToDouble(_price.Replace(",", "."), CultureInfo.InvariantCulture)).Replace(",", ".");
                string amount = (Math.Ceiling(Convert.ToDouble(_amount.Replace(".", ",")) / _minAmount[pairNum]) * _minAmount[pairNum]).ToString().Replace(",", ".");

                const string CreateOrderPath = "/api/v1/order.put_limit";
                string requestTextPrefix = "amount=" + amount + "&api_key=" + _apiKey + "&isfee=1&market=" + _pairNames[pairNum].Replace("_", "/") +
                                        "&price=" + price + "&side=" + (type == "sell" ? 1 : 2);
                string preRequestText = requestTextPrefix + "&secret_key=" + _secretKey;
                string requestBody = requestTextPrefix + "&sign=" + ToMD5(preRequestText).ToUpper();

                WebClient WC = new WebClient();
                dynamic m = JsonConvert.DeserializeObject(WC.DownloadString(HotbitApiUrl + CreateOrderPath + "?" + requestBody));

                Thread.Sleep(200);
                if (m.error != null)
                    throw new Exception(m.error);

                AddMessage("Created " + type + "(" + price + "): amount = " + amount + "| " + (m.error == null ? "True" : "False"));//
                return new Order { ID = m.result.id, Pair = _pairNames[pairNum], Amount = amount, Price = price, Type = type };
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiCreateOrder: " + h);
                    return null;
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                    return null;
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiCreateOrder: " + ex.Message);
                return null;
            }
        }

        private Order ApiCreateOrderP2P2B2B(string pair, string amount, string price, string type) //API
        {
            try
            {
                var unixTimestamp = Convert.ToInt64((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds).ToString();
                const string query = "/api/v1/order/new";

                var request = (HttpWebRequest)WebRequest.Create(OldApiUrl + query);
                var jsonData = 
                    "{\"market\":\"" + pair + "\",\"side\":\"" + type + "\"," +
                    "\"amount\":\"" + amount + "\",\"price\":\"" + price + "\"," +
                    "\"request\":\"" + query + "\",\"nonce\":" + unixTimestamp + "}";
                var data = Encoding.ASCII.GetBytes(jsonData);

                request.Headers.Add("X-TXC-APIKEY", _apiKey[0]);
                request.Headers.Add("X-TXC-SIGNATURE", GetSign512(_secretKey[0], Convert.ToBase64String(data), false));
                request.Headers.Add("X-TXC-PAYLOAD", Convert.ToBase64String(data));

                request.Method = "POST";
                request.ContentType = "application/json";
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                var response = (HttpWebResponse)request.GetResponse();
                var resStream = new StreamReader(response.GetResponseStream()).ReadToEnd();
                dynamic m = JsonConvert.DeserializeObject(resStream);
                Thread.Sleep(200);
                AddMessage("Created " + type + "(" + price  + "): amount = " + amount + "| " + m.success.Value);
                if (!m.success.Value)
                    throw new Exception(JsonConvert.SerializeObject(m.message));

                return new Order { ID = m.result.orderId, Pair = pair, Amount = m.result.amount, Price = m.result.price , Type = type };
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiCreateOrder: " + h);
                    return null;
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                    return null;
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiCreateOrder: " + ex.Message);
                return null;
            }
        }

        private bool ApiCancelOrder(string pair, int id, string hash = "", int walletNum = -1) //API
        {
            if (P2P2B2BChecker.Checked)
                return ApiCancelOrderP2P2B2B(pair, id);
            else if (HotbitChecker.Checked)
                return ApiCancelOrderHotbit(pair, id);
            else if (IdexChecker.Checked)
                return ApiCancelOrderIdex(hash, walletNum);
            return false;
        }

        private bool ApiCancelOrderIdex(string hash, int walletNum = -1)
        {
            if (walletNum == -1)
                AddMessage("ApiCancelOrder: Failed: wallet num was not given");

            try
            {
                string address = _apiKey[walletNum];
                int nonce = Convert.ToInt32(ApiGetNextNonceIdex(walletNum));

                string hash1 = SoliditySha3(
                    new ToBeHashed(HashType.Address, hash),
                    new ToBeHashed(HashType.UInt256, nonce.ToString())
                );

                string prefixHex = BitConverter.ToString(UTF8.GetBytes(prefixEcsign)).Replace("-", "").ToLower();
                string hash2 = keccack256.CalculateHashFromHex("0x" + prefixHex + hash1);

                byte[] hashBytes = new byte[32];
                for (int i = 0; i < 32; i++)
                    hashBytes[i] = byte.Parse(hash2.Substring(i * 2, 2).ToUpper(), NumberStyles.HexNumber);

                EthECDSASignature sign = Ecsign(hashBytes, _secretKey[walletNum]);

                string queryText = "{ \"orderHash\" : \"" + hash + "\", \"address\" : \"" + address + "\", " + 
                                     "\"nonce\" : \"" + nonce.ToString() + "\", \"v\" : " + sign.V[0] + ", " +
                                     "\"r\" : \"0x" + BitConverter.ToString(sign.R).Replace("-", "").ToLower() + "\", " +
                                     "\"s\" : \"0x" + BitConverter.ToString(sign.S).Replace("-", "").ToLower() + "\" }";
                byte[] queryData = ASCII.GetBytes(queryText);

                string CancelOrderPath = "/cancel";

                HttpWebRequest request = WebRequest.Create(IdexApiUrl + CancelOrderPath) as HttpWebRequest;
                request.Method = WebRequestMethods.Http.Post;
                request.ContentType = "application/json";
                request.ContentLength = queryData.Length;
                request.Headers.Add("Payload", Convert.ToBase64String(queryData));

                request.GetRequestStream().Write(queryData, 0, queryData.Length);

                dynamic m;
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                using (StreamReader str = new StreamReader(response.GetResponseStream()))
                    m = JsonConvert.DeserializeObject(str.ReadToEnd());

                if (m.error != null)
                    throw new Exception(m.error);

                AddMessage("Order (hash=" + m.orderHash + ") cancelled");
                return true;
            }
            catch (WebException ex)
            {
                try
                {
                    string h;
                    using (var sr = new StreamReader(ex.Response.GetResponseStream()))
                        h = sr.ReadToEnd();
                    AddMessage("ApiCancelOrder: " + h);
                    return false;
                }
                catch
                {
                    MessageBox.Show("No Internet connection");
                    this.Close();
                    return false;
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiCancelOrder: " + ex.Message);
                return false;
            }
        }

        private bool ApiCancelOrderHotbit(string pair, int id)
        {
            try
            {
                const string CancelOrderPath = "/api/v1/order.cancel";

                string requestTextPrefix = "api_key=" + _apiKey + "&market=" + pair.Replace("_", "/") + "&order_id=" + id;
                string preRequestText = requestTextPrefix + "&secret_key=" + _secretKey;
                string requestBody = requestTextPrefix + "&sign=" + ToMD5(preRequestText).ToUpper();

                WebClient WC = new WebClient();
                dynamic m = JsonConvert.DeserializeObject(WC.DownloadString(HotbitApiUrl + CancelOrderPath + "?" + requestBody));

                if(m.error == null)
                    AddMessage("Order (" + id + ") of pair " + pair + " cancelled");//
                else
                    AddMessage("Failed to cancel order (" + id + ") :" + m.error);//

                return m.error == null;//
            }
            catch (Exception ex)
            {
                AddMessage("ApiCancelOrder: " + ex.Message);
                return false;
            }
        }

        private bool ApiCancelOrderP2P2B2B(string pair, int id) //API
        {
            try
            {
                var unixTimestamp = Convert.ToInt64((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds).ToString();
                const string query = "/api/v1/order/cancel";

                var request = (HttpWebRequest)WebRequest.Create(OldApiUrl + query);
                var jsonData = 
                    "{\"market\":\"" + pair + "\",\"orderId\":" + id + "," +
                    "\"request\":\"" + query + "\",\"nonce\":" + unixTimestamp + "}";

                var data = Encoding.ASCII.GetBytes(jsonData);

                request.Headers.Add("X-TXC-APIKEY", _apiKey[0]);
                request.Headers.Add("X-TXC-SIGNATURE", GetSign512(_secretKey[0], Convert.ToBase64String(data), false));
                request.Headers.Add("X-TXC-PAYLOAD", Convert.ToBase64String(data));

                request.Method = "POST";
                request.ContentType = "application/json";
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                var response = (HttpWebResponse)request.GetResponse();
                var resStream = new StreamReader(response.GetResponseStream()).ReadToEnd();
                dynamic m = JsonConvert.DeserializeObject(resStream);
                Thread.Sleep(200);

                if(m.success.Value)
                    AddMessage("Order (" + id + ") of pair " + pair + " cancelled");
                else
                    AddMessage("Failed to cancel order (" + id + "): " + JsonConvert.SerializeObject(m.message));

                return m.success.Value;
            }
            catch (Exception ex)
            {
                AddMessage("ApiCancelOrder: " + ex.Message);
                return false;
            }
        }

        private void ApiBot(byte pairNum) //API
        {
            if (stopped[pairNum])
                return;

            try
            {
                Thread.Sleep((new Random().Next(Convert.ToInt32(_timerMainRandom[pairNum, _timeZone])) + 1)*1000);
                ApiGetAllOrders();

                // работа с зазором.
                if (_gap[pairNum, 0] == "1" && _lowPrice[pairNum] - _upperPrice[pairNum] < Convert.ToDouble(_gap[pairNum, 1].Replace('.', ',')))
                {
                    // сообщаем, что обнаружили зазор и будем продавать токен.
                    AddMessage($"Operation missed due to: ({_pairNames[pairNum]})lowPrice - upperPrice = " + (_lowPrice[pairNum] - _upperPrice[pairNum]).ToString("0.##########"));
                    foreach (var order in _orders)
                    {
                        if (order.Pair == _pairNames[pairNum] &&
                            order.Price == _upperPrice[pairNum].ToString("0.##########"))
                        {
                            var b = ApiCreateOrder(pairNum, order.Amount,
                                _upperPrice[pairNum].ToString("0.##########").Replace(',', '.'),
                                _typeOrder[0]);
                            AddMessage("Pair = "  + order.Pair + ", price = " + order.Price + ", amount = " + order.Amount);
                            AddMessage("Status: " + b);
                            return;
                        }
                    }
                    return;
                } // конец работы с зазором.

                var rCount = new Random().Next(1, Convert.ToInt32(_countOrders[pairNum, _timeZone]) + 1);

                for (var i = 1; i <= rCount; i++)
                {
                    var typeNow = new Random().Next(2); // Рандомный тип ордера: ask or bid.

                    var min = Convert.ToDouble(_volumeLow[pairNum, _timeZone, typeNow].Replace('.', ','));
                    var max = Convert.ToDouble(_volumeHigh[pairNum, _timeZone, typeNow].Replace('.', ','));

                    var rAmount = GetRandomNumber(min, max, false).Replace(',', '.');
                    /*var isRound = new Random().Next(50); // округлять ли объём ордера.
                    if (isRound < 50 && rAmount.IndexOf(".", StringComparison.Ordinal) > 0)
                        rAmount = rAmount.Substring(0, rAmount.IndexOf(".", StringComparison.Ordinal));*/

                    double[] price = { _lowPrice[pairNum], _upperPrice[pairNum] };
                    var minAskByPercent = (price[0] / 100) * Convert.ToDouble(_minPercent[pairNum, _timeZone].Replace('.', ','));
                    var maxAskByPercent = (price[0] / 100) * Convert.ToDouble(_maxPercent[pairNum, _timeZone].Replace('.', ','));

                    var minBidByPercent = (price[1] / 100) * Convert.ToDouble(_minPercent[pairNum, _timeZone].Replace('.', ','));
                    var maxBidByPercent = (price[1] / 100) * Convert.ToDouble(_maxPercent[pairNum, _timeZone].Replace('.', ','));

                    // ask понижаем на рандомное число из диапазона, bid повышаем.
                    if (typeNow == 0)
                        price[0] = price[0] - Convert.ToDouble(GetRandomNumber(minAskByPercent, maxAskByPercent, true));
                    else
                        price[1] = price[1] + Convert.ToDouble(GetRandomNumber(minBidByPercent, maxBidByPercent, true));

                    // проверка, является ли amount*ask положительным числом.
                    if (Convert.ToDouble(rAmount.Replace('.', ',')) * price[0] > 0.0000001)
                    {
                        if(StopPercentChecker.Checked && price[typeNow] < _relyPrice[pairNum] * (100 - Convert.ToInt32(StopPercent.Value)) / 100)
                        {
                            MessageBox.Show("Цена упала более, чем на " + StopPercent.Value.ToString() + "%. Торги остановлены");
                            stopped[pairNum] = true;
                            return;
                        }
                        
                        Order O = ApiCreateOrder(pairNum, rAmount, price[typeNow].ToString("0.##########").Replace(',', '.'), _typeOrder[typeNow], CurWallet);
                        if (O != null && (O.ID != -1 || O.OrderHash != ""))
                        {
                            AddMessage("Order (" + (IdexChecker.Checked ? "hash=" + O.OrderHash : "id=" + O.ID) + ") created");
                            Thread.Sleep((new Random().Next(Convert.ToInt32(_timerWaitMin[pairNum, _timeZone]), Convert.ToInt32(_timerWait[pairNum, _timeZone]) + 1)) * 1000);
                            ApiGetAllOrders();
                            // существует ли созданный ордер на бирже.

                            if (_orders.First(o => o.Pair == O.Pair && o.Type == O.Type).Equals(O)) //ордер лучший - выставляем встречный ордер
                            {
                                Order CO = ApiCreateOrder(pairNum, O.Amount, O.Price, _typeOrder[typeNow + 1], CurWallet);
                                if (CO != null && CO.ID != -1)
                                    AddMessage("Order (" + (IdexChecker.Checked ? "hash=" + CO.OrderHash : "id=" + CO.ID) + 
                                               ") created (counter to the sell order with " + (IdexChecker.Checked ? "hash=" + O.OrderHash : "id=" + O.ID) + ")");
                                else
                                    AddMessage("Failed to create counter order to (counter to the order with " + (IdexChecker.Checked ? "hash=" + O.OrderHash : "id=" + O.ID) + ")");
                            }
                            else //удаляем ордер с id равным orderId
                                ApiCancelOrder(_pairNames[pairNum], O.ID, O.OrderHash, O.WalletID);
                        }
                        Thread.Sleep((new Random().Next(Convert.ToInt32(_timerOrders[pairNum, _timeZone])) + 1) * 1000);
                    }
                }
            }
            catch (Exception ex)
            {
                AddMessage("ApiBot: " + ex.Message);
                SendMail("Ошибка: " + ex.Message); // отправляем письмо.
                Thread.Sleep(10*60*1000); // спит 10 минут.
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            if (!File.Exists("config.ini")) using (File.Create("config.ini"));
            _iniData = IniParser.ReadFile("config.ini");
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox1.SelectedIndex = 0;
            CheckTimeZone();
            LoadSettings();
            SetMySettings();
            button1.PerformClick();

            ApiGetMinAmount();
            ApiGetTokenInfo();
        }

        private void IdexChecker_CheckedChanged(object sender, EventArgs e)
        {
            label68.Visible = IdexChecker.Checked;
            label69.Visible = IdexChecker.Checked;
            label70.Visible = IdexChecker.Checked;
            label71.Visible = IdexChecker.Checked;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ApiGetBalances();
            ApiGetAllOrders();
            FillTables();
        }
        
        private void SetRelyPrice(int pairNum)
        {
            try
            {
                _relyPrice[pairNum] = Convert.ToDouble(_orders.First(o => o.Pair == _pairNames[pairNum] && o.Type == "sell").Price.Replace(",", "."),
                                                 CultureInfo.InvariantCulture);
                AddMessage("Rely price for pair " + _pairNames[pairNum] + " set to " + _relyPrice[pairNum]);
            }
            catch { }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            ApiCreateOrder(0, textBox1.Text, textBox2.Text, comboBox1.SelectedItem.ToString());
        }

        private void button2_Click(object sender, EventArgs e)
        {
            stopped[0] = false;
            SetMySettings();
            SetRelyPrice(0);
            timer1.Interval = Convert.ToInt32(_timerMain[0, _timeZone]) * 1000;
            timer1.Enabled = true;
            StopPercent2.TabPages[1].Enabled = false;
            StopPercent2.TabPages[2].Enabled = false;
            Text = @"P2pb2b (working pair ETH)";

            P2P2B2BChecker.Enabled = false;
            HotbitChecker.Enabled = false;
            IdexChecker.Enabled = false;
            ChangeToken.Enabled = false;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            stopped[0] = true;
            timer1.Enabled = false;
            StopPercent2.TabPages[1].Enabled = true;
            StopPercent2.TabPages[2].Enabled = true;
            Text = @"P2pb2b (stopped)";

            P2P2B2BChecker.Enabled = true;
            HotbitChecker.Enabled = true;
            IdexChecker.Enabled = true;
            ChangeToken.Enabled = true;
        }

        private void button7_Click(object sender, EventArgs e)
        {
            stopped[1] = false;
            SetMySettings();
            SetRelyPrice(1);
            timer2.Interval = Convert.ToInt32(_timerMain[1, _timeZone]) * 1000;
            timer2.Enabled = true;
            StopPercent2.TabPages[0].Enabled = false;
            StopPercent2.TabPages[2].Enabled = false;
            Text = @"P2pb2b (working pair BTC)";

            P2P2B2BChecker.Enabled = false;
            HotbitChecker.Enabled = false;
            IdexChecker.Enabled = false;
            ChangeToken.Enabled = false;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            stopped[1] = true;
            timer2.Enabled = false;
            StopPercent2.TabPages[0].Enabled = true;
            StopPercent2.TabPages[2].Enabled = true;
            Text = @"P2pb2b (stopped)";

            P2P2B2BChecker.Enabled = true;
            HotbitChecker.Enabled = true;
            IdexChecker.Enabled = true;
            ChangeToken.Enabled = true;
        }

        private void Button9_Click(object sender, EventArgs e)
        {
            stopped[2] = false;
            SetMySettings();
            SetRelyPrice(2);
            timer3.Interval = Convert.ToInt32(_timerMain[2, _timeZone]) * 1000;
            timer3.Enabled = true;
            StopPercent2.TabPages[0].Enabled = false;
            StopPercent2.TabPages[1].Enabled = false;
            Text = @"P2pb2b (working pair USD)";

            P2P2B2BChecker.Enabled = false;
            HotbitChecker.Enabled = false;
            IdexChecker.Enabled = false;
            ChangeToken.Enabled = false;
        }

        private void Button8_Click(object sender, EventArgs e)
        {
            stopped[2] = true;
            timer3.Enabled = false;
            StopPercent2.TabPages[0].Enabled = true;
            StopPercent2.TabPages[1].Enabled = true;
            Text = @"P2pb2b (stopped)";

            P2P2B2BChecker.Enabled = true;
            HotbitChecker.Enabled = true;
            IdexChecker.Enabled = true;
            ChangeToken.Enabled = true;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            CheckTimeZone();
            if (timer1.Interval != Convert.ToInt32(_timerMain[0, _timeZone]) * 1000)
            {
                timer1.Stop();
                timer1.Interval = Convert.ToInt32(_timerMain[0, _timeZone]) * 1000;
                timer1.Start();
                AddMessage("Change timeZone on #" + _timeZone);
            }

            if (_thread1 == null)
            {
                _thread1 = new Thread(() => ApiBot(0));
                _thread1.Start();
            }
            else if (!_thread1.IsAlive) _thread1 = null;
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            CheckTimeZone();
            if (timer2.Interval != Convert.ToInt32(_timerMain[1, _timeZone]) * 1000)
            {
                timer2.Stop();
                timer2.Interval = Convert.ToInt32(_timerMain[1, _timeZone]) * 1000;
                timer2.Start();
                AddMessage("Change timeZone on #" + _timeZone);
            }

            if (_thread1 == null)
            {
                _thread1 = new Thread(() => ApiBot(1));
                _thread1.Start();
            }
            else if (!_thread1.IsAlive) _thread1 = null;
        }

        private void Timer3_Tick(object sender, EventArgs e)
        {
            CheckTimeZone();
            if (timer3.Interval != Convert.ToInt32(_timerMain[2, _timeZone]) * 1000)
            {
                timer3.Stop();
                timer3.Interval = Convert.ToInt32(_timerMain[2, _timeZone]) * 1000;
                timer3.Start();
                AddMessage("Change timeZone on #" + _timeZone);
            }

            if (_thread1 == null)
            {
                _thread1 = new Thread(() => ApiBot(2));
                _thread1.Start();
            }
            else if (!_thread1.IsAlive) _thread1 = null;
        }

        private void CheckTimeZone()
        {
            var hourNow = DateTime.Now.Hour;

            if (hourNow == 9)
            {
                if (!_reportSent)
                {
                    SendMail("Баланс \"" + MainName + "\": " + _balances[3]);
                    _reportSent = true;
                }
            }
            else _reportSent = false;

            if (hourNow >= 9 && hourNow < 23)
                _timeZone = 0;
            else
                _timeZone = 1;
            tabControl2.SelectedIndex = _timeZone;
            tabControl3.SelectedIndex = _timeZone;
        }

        private void SendMail(string body)
        { // почта нужна gmail
            try
            {
                //if (checkBoxMail.Checked)
                {
                    var mail = new MailMessage();
                    var smtpServer = new SmtpClient("smtp.gmail.com");
                    mail.From = new MailAddress("login@gmail.com"); // от кого. надо поменять на свою почту, которая будет СЛАТЬ письмо.
                    mail.To.Add("to@gmail.com"); // кому. надо поменять на свою почту, которая будет ПРИНИМАТЬ письмо.
                    mail.Subject = "P2pb2b bot";
                    mail.Body = body;
                    mail.IsBodyHtml = true;

                    smtpServer.Port = 587;
                    smtpServer.Credentials = new NetworkCredential("login", "pass"); // логин - всё что до знака @, пароль от почы. Тут данные почты, которая будет СЛАТЬ письмо.
                    smtpServer.EnableSsl = true;

                    smtpServer.Send(mail);
                    AddMessage("Письмо отправлено.");
                }
            }
            catch (Exception ex)
            {
                AddMessage("SendMail: " + ex.Message);
            }
        }

        private static string GetSign512(string secret, string message, bool isBase64)
        {
            var encoding = new ASCIIEncoding();
            string signature;

            byte[] keyByte = encoding.GetBytes(secret);

            byte[] messageBytes = encoding.GetBytes(message);
            if (isBase64) messageBytes = encoding.GetBytes(Convert.ToBase64String(messageBytes));
            using (var hash = new HMACSHA512(keyByte))
            {
                byte[] signature1 = hash.ComputeHash(messageBytes);
                signature = BitConverter.ToString(signature1).Replace("-", "").ToLower();
            }
            return signature;
        }

        private void textBox49_TextChanged(object sender, EventArgs e)
        {
            int i = Apis.ToList().IndexOf(sender as TextBox);
            _apiKey[i] = Apis[i].Text;
            _iniData["Account"]["ApiKey" + i] = _apiKey[i];
            IniParser.WriteFile("config.ini", _iniData);
        }

        private void textBox50_TextChanged(object sender, EventArgs e)
        {
            int i = Secures.ToList().IndexOf(sender as TextBox);
            _secretKey[i] = Secures[i].Text;
            _iniData["Account"]["SecretKey" + i] = _secretKey[i];
            IniParser.WriteFile("config.ini", _iniData);
        }

        private void ExchangeChecker_CheckedChanged(object sender, EventArgs e)
        {
            button4.PerformClick();
            button5.PerformClick();
            button8.PerformClick();

            _orders = new List<Order>();

            ApiGetBalances();
            ApiGetAllOrders();
            FillTables();
        }

        private void ChangeToken_Click(object sender, EventArgs e)
        {
            TokenChangeForm TCF = new TokenChangeForm();
            TCF.ShowDialog();
            MainName = TCF.Token;
            _pairNames = new string[] { MainName + "_ETH", MainName + "_BTC", MainName + "_USD" };
            button1.PerformClick();
        }
    }
}
