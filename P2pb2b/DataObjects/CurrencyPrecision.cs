using Newtonsoft.Json;

namespace P2pb2b.DataObjects
{
    public class CurrencyPrecision
    {
        [JsonProperty("name")]
        public string PairName { get; private set; }

        [JsonProperty("stock")]
        public string ChangingCurrency { get; private set; }

        [JsonProperty("money")]
        public string RelyCurrency { get; private set; }

        [JsonProperty("fee_prec")]
        public int FeePrecision { get; private set; }

        [JsonProperty("stock_prec")]
        public int ChangingPrecision { get; private set; }

        [JsonProperty("money_prec")]
        public int RelyPrecision { get; private set; }

        [JsonProperty("min_amount")]
        public string MinAmount { get; private set; }

        public CurrencyPrecision(string name, string stock, int fee_prec, string money, int stock_prec, string min_amount, int money_prec)
        {
            PairName = name;
            ChangingCurrency = stock;
            RelyCurrency = money;

            FeePrecision = fee_prec;
            ChangingPrecision = stock_prec;
            RelyPrecision = money_prec;

            MinAmount = min_amount;
        }
    }
}
