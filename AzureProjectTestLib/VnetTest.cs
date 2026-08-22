#pragma warning disable CS0618
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using AzureProjectTestLib.Helper;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace AzureProjectTestLib;

[GameClass(3)]
[Parallelizable(ParallelScope.Children)]
public class VnetTests
{
    [GameTask(
        "Create VNet 'projVnet1Prod' in the Azure Southeast Asia region with first address prefix '10.0.0.0/16', and VNet 'projVnet2Prod' in the Azure East Asia region with first address prefix '10.1.0.0/16'.",
        2, 10, 1)]
    [Test]
    public void Test01_Have2VnetsIn2Regions()
    {
        using var scope = new TestScope();
        Assert.IsNotNull(scope.Vnet1);
        Assert.AreEqual("southeastasia", scope.Vnet1.Location.ToString());
        Assert.IsNotNull(scope.Vnet2);
        Assert.AreEqual("eastasia", scope.Vnet2.Location.ToString());
    }

    [GameTask(1)]
    [Test]
    public void Test02_VnetAddressSpace()
    {
        using var scope = new TestScope();
        Assert.AreEqual("10.0.0.0/16", scope.Vnet1.AddressSpace.AddressPrefixes[0], "Vnet1 Address space 10.0.0.0/16");
        Assert.AreEqual("10.1.0.0/16", scope.Vnet2.AddressSpace.AddressPrefixes[0], "Vnet2 Address space 10.1.0.0/16");
    }

    [GameTask(
    "Create 2 subnets in vnet 'projVnet1Prod' with CIDRs 10.0.1.0/24 and 10.0.0.0/24; then create 2 subnets in vnet 'projVnet2Prod' with CIDRs 10.1.1.0/24 and 10.1.0.0/24.",
    5, 20, 2)]
    [Test]
    public void Test03_VnetWith2Subnets()
    {
        using var scope = new TestScope();
        Assert.AreEqual(2, scope.Vnet1.Subnets.Count, "2 subnets");
        Assert.AreEqual(2, scope.Vnet2.Subnets.Count, "2 subnets");
    }

    [GameTask(2)]
    [Test]
    public void Test04_Vnet1SubnetsCidr()
    {
        using var scope = new TestScope();
        var publicSubnet = scope.GetVnet1PublicSubnet();
        var privateSubnet = scope.GetVnet1PrivateSubnet();
        Assert.IsNotNull(publicSubnet);
        Assert.IsNotNull(privateSubnet);
    }

    [GameTask(2)]
    [Test]
    public void Test05_Vnet2SubnetsCidr()
    {
        using var scope = new TestScope();
        var publicSubnet = scope.GetVnet2PublicSubnet();
        var privateSubnet = scope.GetVnet2PrivateSubnet();
        Assert.IsNotNull(publicSubnet);
        Assert.IsNotNull(privateSubnet);
    }

    [GameTask("In vnet 'projVnet1Prod', for subnet 10.0.1.0/24, attach a route table with routes: 10.0.0.0/16 -> VnetLocal and 0.0.0.0/0 -> Internet.", 5, 10)]
    [Test]
    public void Test06_Vnet1PublicSubnetsRoutes()
    {
        using var scope = new TestScope();
        var publicSubnet = scope.GetVnet1PublicSubnet();
        var routeTable = scope.GetRouteTable(publicSubnet);

        var localRoute =
            routeTable?.Routes.FirstOrDefault(c =>
                c.AddressPrefix == "10.0.0.0/16" && c.NextHopType.ToString() == "VnetLocal");
        var internetRoute =
            routeTable?.Routes.FirstOrDefault(c =>
                c.AddressPrefix == "0.0.0.0/0" && c.NextHopType.ToString() == "Internet");

        Assert.IsNotNull(localRoute);
        Assert.IsNotNull(internetRoute);
    }

    [GameTask("In vnet 'projVnet2Prod', for subnet 10.1.1.0/24, attach a route table with routes: 10.1.0.0/16 -> VnetLocal and 0.0.0.0/0 -> Internet.", 5, 10)]
    [Test]
    public void Test07_Vnet2PublicSubnetsRoutes()
    {
        using var scope = new TestScope();
        var publicSubnet = scope.GetVnet2PublicSubnet();
        var routeTable = scope.GetRouteTable(publicSubnet);

        var localRoute =
            routeTable?.Routes.FirstOrDefault(c =>
                c.AddressPrefix == "10.1.0.0/16" && c.NextHopType.ToString() == "VnetLocal");
        var internetRoute =
            routeTable?.Routes.FirstOrDefault(c =>
                c.AddressPrefix == "0.0.0.0/0" && c.NextHopType.ToString() == "Internet");

        Assert.IsNotNull(localRoute);
        Assert.IsNotNull(internetRoute);
    }

    [GameTask("In vnet 'projVnet1Prod', for subnet 10.0.0.0/24, attach a route table with a VnetLocal route to 10.0.0.0/16 (private subnet).", 5, 10)]
    [Test]
    public void Test08_Vnet1PrivateSubnetsRoutes()
    {
        using var scope = new TestScope();
        var privateSubnet = scope.GetVnet1PrivateSubnet();
        var routeTable = scope.GetRouteTable(privateSubnet);

        var localRoute =
            routeTable?.Routes.FirstOrDefault(c =>
                c.AddressPrefix == "10.0.0.0/16" && c.NextHopType.ToString() == "VnetLocal");
        Assert.IsNotNull(localRoute);
    }

    [GameTask("In vnet 'projVnet2Prod', for subnet 10.1.0.0/24, attach a route table with a VnetLocal route to 10.1.0.0/16 (private subnet).", 5, 10)]
    [Test]
    public void Test09_Vnet2PrivateSubnetsRoutes()
    {
        using var scope = new TestScope();
        var privateSubnet = scope.GetVnet2PrivateSubnet();
        var routeTable = scope.GetRouteTable(privateSubnet);

        var localRoute =
            routeTable?.Routes.FirstOrDefault(c =>
                c.AddressPrefix == "10.1.0.0/16" && c.NextHopType.ToString() == "VnetLocal");
        Assert.IsNotNull(localRoute);
    }

    [GameTask("Attach a Standard SKU NAT Gateway in availability zone 1 to subnet 10.0.1.0/24.", 5, 10)]
    [Test]
    public void Test10_Vnet1PublicSubnetsNatGateway()
    {
        using var scope = new TestScope();
        var publicSubnet = scope.GetVnet1PublicSubnet();
        Assert.IsNotNull(publicSubnet);
        var natGateway = scope.GetNatGateway(publicSubnet!);

        Assert.IsNotNull(natGateway);
        Assert.AreEqual("Standard", natGateway!.SkuName.ToString());
        Assert.AreEqual("1", natGateway.Zones[0]);
    }

    [GameTask("Add a Virtual Network Peering from 'projVnet1Prod' to 'projVnet2Prod' (remote). Allow Forwarded Traffic and Virtual Network Access; do not allow Gateway Transit.", 5, 10)]
    [Test]
    public void Test11_VnetGlobalPeering()
    {
        using var scope = new TestScope();
        var virtualNetworkPeering = scope.Vnet1.VirtualNetworkPeerings[0];
        Assert.IsNotNull(virtualNetworkPeering);
        Assert.AreEqual(virtualNetworkPeering.RemoteVirtualNetwork.Id, scope.Vnet2.Id);
        Assert.IsTrue(virtualNetworkPeering.AllowForwardedTraffic);
        Assert.IsTrue(virtualNetworkPeering.AllowVirtualNetworkAccess);
        Assert.IsFalse(virtualNetworkPeering.AllowGatewayTransit);
    }

    [GameTask("Attach an NSG to subnet 10.0.1.0/24 containing: (1) an Allow TCP inbound rule from source address and port '*' to destination 10.0.1.0/24 on port 80 with priority 201; (2) an Allow TCP outbound rule from source address and port '*' to destination address and port '*' with priority 100.", 5, 10)]
    [Test]
    public void Test12_Vnet1PublicSubnetNetworkSecurityGroup()
    {
        using var scope = new TestScope();
        var publicSubnet = scope.GetVnet1PublicSubnet();
        Assert.IsNotNull(publicSubnet);
        var resolvedPublicSubnet = publicSubnet!;
        var networkSecurityGroup = scope.GetNetworkSecurityGroup(resolvedPublicSubnet);
        Assert.IsNotNull(networkSecurityGroup);
        var resolvedNetworkSecurityGroup = networkSecurityGroup!;

        var allowHttpInbound = resolvedNetworkSecurityGroup.SecurityRules.FirstOrDefault(c => c.DestinationPortRange == "80");
        Assert.IsNotNull(allowHttpInbound);
        var resolvedAllowHttpInbound = allowHttpInbound!;
        Assert.AreEqual("Allow", resolvedAllowHttpInbound.Access.ToString());
        Assert.AreEqual("Inbound", resolvedAllowHttpInbound.Direction.ToString());
        Assert.AreEqual("*", resolvedAllowHttpInbound.SourcePortRange);
        Assert.AreEqual("*", resolvedAllowHttpInbound.SourceAddressPrefix);
        Assert.AreEqual("TCP", Convert.ToString(resolvedAllowHttpInbound.Protocol)?.ToUpperInvariant());
        Assert.AreEqual(201, resolvedAllowHttpInbound.Priority);
        Assert.IsTrue(SecurityRuleTargetsSubnet(resolvedAllowHttpInbound, resolvedPublicSubnet));

        var allowAllTcpOutbound = resolvedNetworkSecurityGroup.SecurityRules.FirstOrDefault(c => c.DestinationPortRange == "*");
        Assert.IsNotNull(allowAllTcpOutbound);
        var resolvedAllowAllTcpOutbound = allowAllTcpOutbound!;
        Assert.AreEqual("Allow", resolvedAllowAllTcpOutbound.Access.ToString());
        Assert.AreEqual("Outbound", resolvedAllowAllTcpOutbound.Direction.ToString());
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.SourcePortRange);
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.SourceAddressPrefix);
        Assert.AreEqual("TCP", Convert.ToString(resolvedAllowAllTcpOutbound.Protocol)?.ToUpperInvariant());
        Assert.AreEqual(100, resolvedAllowAllTcpOutbound.Priority);
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.DestinationAddressPrefix);
    }

    [GameTask("Attach an NSG to subnet 10.1.1.0/24 containing: (1) an Allow TCP inbound rule from source address and port '*' to destination 10.1.1.0/24 on port 80 with priority 201; (2) an Allow TCP outbound rule from source address and port '*' to destination address and port '*' with priority 100.", 5, 10)]
    [Test]
    public void Test13_Vnet2PublicSubnetNetworkSecurityGroup()
    {
        using var scope = new TestScope();
        var publicSubnet = scope.GetVnet2PublicSubnet();
        Assert.IsNotNull(publicSubnet);
        var resolvedPublicSubnet = publicSubnet!;
        var networkSecurityGroup = scope.GetNetworkSecurityGroup(resolvedPublicSubnet);
        Assert.IsNotNull(networkSecurityGroup);
        var resolvedNetworkSecurityGroup = networkSecurityGroup!;

        var allowHttpInbound = resolvedNetworkSecurityGroup.SecurityRules.FirstOrDefault(c => c.DestinationPortRange == "80");
        Assert.IsNotNull(allowHttpInbound);
        var resolvedAllowHttpInbound = allowHttpInbound!;
        Assert.AreEqual("Allow", resolvedAllowHttpInbound.Access.ToString());
        Assert.AreEqual("Inbound", resolvedAllowHttpInbound.Direction.ToString());
        Assert.AreEqual("*", resolvedAllowHttpInbound.SourcePortRange);
        Assert.AreEqual("*", resolvedAllowHttpInbound.SourceAddressPrefix);
        Assert.AreEqual("TCP", Convert.ToString(resolvedAllowHttpInbound.Protocol)?.ToUpperInvariant());
        Assert.AreEqual(201, resolvedAllowHttpInbound.Priority);
        Assert.IsTrue(SecurityRuleTargetsSubnet(resolvedAllowHttpInbound, resolvedPublicSubnet));

        var allowAllTcpOutbound = resolvedNetworkSecurityGroup.SecurityRules.FirstOrDefault(c => c.DestinationPortRange == "*");
        Assert.IsNotNull(allowAllTcpOutbound);
        var resolvedAllowAllTcpOutbound = allowAllTcpOutbound!;
        Assert.AreEqual("Allow", resolvedAllowAllTcpOutbound.Access.ToString());
        Assert.AreEqual("Outbound", resolvedAllowAllTcpOutbound.Direction.ToString());
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.SourcePortRange);
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.SourceAddressPrefix);
        Assert.AreEqual("TCP", Convert.ToString(resolvedAllowAllTcpOutbound.Protocol)?.ToUpperInvariant());
        Assert.AreEqual(100, resolvedAllowAllTcpOutbound.Priority);
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.DestinationAddressPrefix);
    }

    [GameTask("Attach an NSG to subnet 10.0.0.0/24 containing: (1) an Allow TCP inbound rule from source subnet 10.1.0.0/24 and source port '*' to destination 10.0.0.0/24 on port 80 with priority 201; (2) an Allow TCP outbound rule from source address and port '*' to destination address and port '*' with priority 100.", 5, 10)]
    [Test]
    public void Test14_Vnet1PrivateSubnetNetworkSecurityGroup()
    {
        using var scope = new TestScope();
        var privateSubnet1 = scope.GetVnet1PrivateSubnet();
        Assert.IsNotNull(privateSubnet1);
        var resolvedPrivateSubnet1 = privateSubnet1!;
        var networkSecurityGroup = scope.GetNetworkSecurityGroup(resolvedPrivateSubnet1);
        Assert.IsNotNull(networkSecurityGroup);
        var resolvedNetworkSecurityGroup = networkSecurityGroup!;

        var allowAllTcpOutbound = resolvedNetworkSecurityGroup.SecurityRules.FirstOrDefault(c => c.DestinationPortRange == "*");
        Assert.IsNotNull(allowAllTcpOutbound);
        var resolvedAllowAllTcpOutbound = allowAllTcpOutbound!;
        Assert.AreEqual("Allow", resolvedAllowAllTcpOutbound.Access.ToString());
        Assert.AreEqual("Outbound", resolvedAllowAllTcpOutbound.Direction.ToString());
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.SourcePortRange);
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.SourceAddressPrefix);
        Assert.AreEqual("TCP", Convert.ToString(resolvedAllowAllTcpOutbound.Protocol)?.ToUpperInvariant());
        Assert.AreEqual(100, resolvedAllowAllTcpOutbound.Priority);
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.DestinationAddressPrefix);

        var vnet2PrivateSubnet = scope.GetVnet2PrivateSubnet();
        Assert.IsNotNull(vnet2PrivateSubnet);
        var crossVnetInbound = resolvedNetworkSecurityGroup.SecurityRules.FirstOrDefault(c =>
            SecurityRuleSourcesSubnet(c, vnet2PrivateSubnet!));
        Assert.IsNotNull(crossVnetInbound);
        var resolvedCrossVnetInbound = crossVnetInbound!;
        Assert.AreEqual("Allow", resolvedCrossVnetInbound.Access.ToString());
        Assert.AreEqual("Inbound", resolvedCrossVnetInbound.Direction.ToString());
        Assert.AreEqual("*", resolvedCrossVnetInbound.SourcePortRange);
        Assert.AreEqual("TCP", Convert.ToString(resolvedCrossVnetInbound.Protocol)?.ToUpperInvariant());
        Assert.AreEqual("80", resolvedCrossVnetInbound.DestinationPortRange);
        Assert.AreEqual(201, resolvedCrossVnetInbound.Priority);
        Assert.IsTrue(SecurityRuleTargetsSubnet(resolvedCrossVnetInbound, resolvedPrivateSubnet1));
    }

    [GameTask("Attach an NSG to subnet 10.1.0.0/24 containing: (1) an Allow TCP inbound rule from source subnet 10.0.0.0/24 and source port '*' to destination 10.1.0.0/24 on port 80 with priority 201; (2) an Allow TCP outbound rule from source address and port '*' to destination address and port '*' with priority 100.", 5, 10)]
    [Test]
    public void Test15_Vnet2PrivateSubnetNetworkSecurityGroup()
    {
        using var scope = new TestScope();
        var privateSubnet2 = scope.GetVnet2PrivateSubnet();
        Assert.IsNotNull(privateSubnet2);
        var resolvedPrivateSubnet2 = privateSubnet2!;
        var networkSecurityGroup = scope.GetNetworkSecurityGroup(resolvedPrivateSubnet2);
        Assert.IsNotNull(networkSecurityGroup);
        var resolvedNetworkSecurityGroup = networkSecurityGroup!;

        var allowAllTcpOutbound = resolvedNetworkSecurityGroup.SecurityRules.FirstOrDefault(c => c.DestinationPortRange == "*");
        Assert.IsNotNull(allowAllTcpOutbound);
        var resolvedAllowAllTcpOutbound = allowAllTcpOutbound!;
        Assert.AreEqual("Allow", resolvedAllowAllTcpOutbound.Access.ToString());
        Assert.AreEqual("Outbound", resolvedAllowAllTcpOutbound.Direction.ToString());
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.SourcePortRange);
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.SourceAddressPrefix);
        Assert.AreEqual("TCP", Convert.ToString(resolvedAllowAllTcpOutbound.Protocol)?.ToUpperInvariant());
        Assert.AreEqual(100, resolvedAllowAllTcpOutbound.Priority);
        Assert.AreEqual("*", resolvedAllowAllTcpOutbound.DestinationAddressPrefix);

        var vnet1PrivateSubnet = scope.GetVnet1PrivateSubnet();
        Assert.IsNotNull(vnet1PrivateSubnet);
        var crossVnetInbound = resolvedNetworkSecurityGroup.SecurityRules.FirstOrDefault(c =>
            SecurityRuleSourcesSubnet(c, vnet1PrivateSubnet!));
        Assert.IsNotNull(crossVnetInbound);
        var resolvedCrossVnetInbound = crossVnetInbound!;
        Assert.AreEqual("Allow", resolvedCrossVnetInbound.Access.ToString());
        Assert.AreEqual("Inbound", resolvedCrossVnetInbound.Direction.ToString());
        Assert.AreEqual("*", resolvedCrossVnetInbound.SourcePortRange);
        Assert.AreEqual("TCP", Convert.ToString(resolvedCrossVnetInbound.Protocol)?.ToUpperInvariant());
        Assert.AreEqual("80", resolvedCrossVnetInbound.DestinationPortRange);
        Assert.AreEqual(201, resolvedCrossVnetInbound.Priority);
        Assert.IsTrue(SecurityRuleTargetsSubnet(resolvedCrossVnetInbound, resolvedPrivateSubnet2));
    }

    private static string? GetSubnetAddressPrefix(SubnetData? subnet)
    {
        return !string.IsNullOrEmpty(subnet?.AddressPrefix)
            ? subnet.AddressPrefix
            : subnet?.AddressPrefixes?.FirstOrDefault();
    }

    private static bool SecurityRuleSourcesSubnet(
        SecurityRuleData securityRule,
        SubnetData subnet)
    {
        var subnetAddressPrefix = GetSubnetAddressPrefix(subnet);
        return subnetAddressPrefix != null &&
               (securityRule.SourceAddressPrefix == subnetAddressPrefix ||
                (securityRule.SourceAddressPrefixes?.Contains(
                    subnetAddressPrefix) ?? false));
    }

    private static bool SecurityRuleTargetsSubnet(
        SecurityRuleData securityRule,
        SubnetData subnet)
    {
        var subnetAddressPrefix = GetSubnetAddressPrefix(subnet);
        if (subnetAddressPrefix == null)
        {
            return false;
        }

        return securityRule.DestinationAddressPrefix == subnetAddressPrefix ||
               (securityRule.DestinationAddressPrefixes?.Contains(
                   subnetAddressPrefix) ?? false);
    }

    private sealed class TestScope : IDisposable
    {
        public readonly ArmClient Client;
        private readonly VirtualNetworkResource vnet1Resource;
        private readonly VirtualNetworkResource vnet2Resource;

        public TestScope()
        {
            var config = new Config();
            Client = config.ArmClient;

            var resourceGroup = config.GetResourceGroupResource(Constants.ResourceGroupName);
            vnet1Resource = resourceGroup.GetVirtualNetworks().Get(Constants.Vnet1Name).Value;
            vnet2Resource = resourceGroup.GetVirtualNetworks().Get(Constants.Vnet2Name).Value;
        }

        public VirtualNetworkData Vnet1 => vnet1Resource.Data;
        public VirtualNetworkData Vnet2 => vnet2Resource.Data;

        public void Dispose()
        {
        }

        public SubnetData? GetVnet1PublicSubnet()
        {
            return Vnet1.Subnets.FirstOrDefault(
                c => GetSubnetAddressPrefix(c) == "10.0.1.0/24");
        }

        public SubnetData? GetVnet2PublicSubnet()
        {
            return Vnet2.Subnets.FirstOrDefault(
                c => GetSubnetAddressPrefix(c) == "10.1.1.0/24");
        }

        public SubnetData? GetVnet1PrivateSubnet()
        {
            return Vnet1.Subnets.FirstOrDefault(
                c => GetSubnetAddressPrefix(c) == "10.0.0.0/24");
        }

        public SubnetData? GetVnet2PrivateSubnet()
        {
            return Vnet2.Subnets.FirstOrDefault(
                c => GetSubnetAddressPrefix(c) == "10.1.0.0/24");
        }

        public RouteTableData? GetRouteTable(SubnetData? subnet)
        {
            var routeTableId = subnet?.RouteTable?.Id;
            if (routeTableId is null)
            {
                return null;
            }

            return Client.GetRouteTableResource(routeTableId).Get().Value.Data;
        }

        public NetworkSecurityGroupData? GetNetworkSecurityGroup(SubnetData? subnet)
        {
            var networkSecurityGroupId = subnet?.NetworkSecurityGroup?.Id;
            if (networkSecurityGroupId is null)
            {
                return null;
            }

            return Client.GetNetworkSecurityGroupResource(networkSecurityGroupId).Get().Value.Data;
        }

        public NatGatewayData? GetNatGateway(SubnetData? subnet)
        {
            var natGatewayId = subnet?.NatGatewayId;
            if (natGatewayId is null)
            {
                return null;
            }

            return Client.GetNatGatewayResource(natGatewayId).Get().Value.Data;
        }
    }
}
#pragma warning restore CS0618
