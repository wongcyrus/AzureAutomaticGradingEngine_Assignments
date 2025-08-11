using System.Runtime.Serialization;


namespace GraderFunctionApp.Models;
[DataContract]
public class AppPrincipal : JsonBase<AppPrincipal>
{
    [DataMember] public string? appId;
    [DataMember] public string? displayName;
    [DataMember] public string? password;
    [DataMember] public string? tenant;
}