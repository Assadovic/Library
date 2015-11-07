using System;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "UploadItem", Namespace = "http://Library/Net/Outopos")]
    sealed class UploadItem
    {
        [DataMember(Name = "Type")]
        public string Type { get; set; }

        [DataMember(Name = "BroadcastMessage")]
        public BroadcastMessage BroadcastMessage { get; set; }

        [DataMember(Name = "UnicastMessage")]
        public UnicastMessage UnicastMessage { get; set; }

        [DataMember(Name = "MulticastMessage")]
        public MulticastMessage MulticastMessage { get; set; }

        [DataMember(Name = "DigitalSignature")]
        public DigitalSignature DigitalSignature { get; set; }

        [DataMember(Name = "MiningLimit")]
        public int MiningLimit { get; set; }

        [DataMember(Name = "MiningTime")]
        public TimeSpan MiningTime { get; set; }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey { get; set; }
    }
}
