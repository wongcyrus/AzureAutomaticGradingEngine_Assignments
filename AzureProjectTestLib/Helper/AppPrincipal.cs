using System.Runtime.Serialization;

namespace AzureProjectTestLib.Helper;

// ReSharper disable InconsistentNaming
[DataContract]
public class AppPrincipal : JsonBase<AppPrincipal>
{
    [DataMember] public string appId = string.Empty;
    [DataMember] public string displayName = string.Empty;
    [DataMember] public string password = string.Empty;
    [DataMember] public string tenant = string.Empty;
}