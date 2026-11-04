using Microsoft.Extensions.Configuration;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class ClearingHouseCatalogTests
{
    /// <summary>Same shape as the legacy appsettings: non-clearing-house children (Blob, StediAccounts) are skipped.</summary>
    private static readonly Dictionary<string, string?> LegacyShape = new()
    {
        ["Clearinghouses:Stedi:Id"] = "8",
        ["Clearinghouses:Stedi:Title"] = "Stedi",
        ["Clearinghouses:Stedi:Hosts"] = "stedi-hosts",
        ["Clearinghouses:Stedi:Port"] = "stedi-port",
        ["Clearinghouses:Stedi:UserName"] = "stedi-username",
        ["Clearinghouses:Stedi:UserPassword"] = "stedi-password",
        ["Clearinghouses:Stedi:DownloadDirectory"] = "stedi-downloadDirectory",
        ["Clearinghouses:Stedi:BaseUrl"] = "stedi-api-url",
        ["Clearinghouses:Availity:Id"] = "1",
        ["Clearinghouses:Availity:Title"] = "Availity",
        ["Clearinghouses:Availity:Hosts"] = "availity-hosts",
        ["Clearinghouses:Availity:Port"] = "availity-port",
        ["Clearinghouses:Availity:UserName"] = "availity-username",
        ["Clearinghouses:Availity:UserPassword"] = "availity-password",
        ["Clearinghouses:Availity:DownloadDirectory"] = "availity-downloadDirectory",
        ["Clearinghouses:Availity:HostKeyFingerprint"] = "SHA256:abc",
        ["Clearinghouses:Blob:ConnectionString"] = "x",
        ["Clearinghouses:StediAccounts:26485:UserName"] = "u",
    };

    [Fact]
    public void Bind_keeps_only_children_with_id_and_title()
    {
        var bound = ClearingHouseCatalogOptions.Bind(Build(LegacyShape));

        bound.Select(settings => settings.SectionKey).Should().BeEquivalentTo("Stedi", "Availity");
        var availity = bound.Single(settings => settings.Id == 1);
        availity.Title.Should().Be("Availity");
        availity.Hosts.Should().Be("availity-hosts");
        availity.HostKeyFingerprint.Should().Be("SHA256:abc");
        availity.IsComplete.Should().BeTrue();
        FluentActions.Invoking(() => ClearingHouseCatalogOptions.Bind(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Options_are_valid_only_when_every_clearing_house_is_complete()
    {
        new ClearingHouseCatalogOptions().IsValid.Should().BeFalse();
        new ClearingHouseCatalogOptions { ClearingHouses = ClearingHouseCatalogOptions.Bind(Build(LegacyShape)) }.IsValid.Should().BeTrue();
        new ClearingHouseCatalogOptions { ClearingHouses = [new ClearingHouseSettings { Id = 1, Title = "A" }] }.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(nameof(ClearingHouseSettings.Hosts))]
    [InlineData(nameof(ClearingHouseSettings.Port))]
    [InlineData(nameof(ClearingHouseSettings.UserName))]
    [InlineData(nameof(ClearingHouseSettings.UserPassword))]
    [InlineData(nameof(ClearingHouseSettings.DownloadDirectory))]
    [InlineData(nameof(ClearingHouseSettings.Title))]
    public void Settings_are_incomplete_when_a_required_value_is_blank(string property)
    {
        var settings = Complete();
        typeof(ClearingHouseSettings).GetProperty(property)!.SetValue(settings, " ");

        settings.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Settings_need_a_positive_id()
    {
        var settings = Complete();
        settings.Id = 0;

        settings.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Catalog_finds_by_id_and_first_duplicate_wins()
    {
        var first = Complete();
        var duplicate = Complete();
        duplicate.Title = "Duplicate";
        var catalog = new ConfiguredClearingHouseCatalog(Microsoft.Extensions.Options.Options.Create(new ClearingHouseCatalogOptions { ClearingHouses = [first, duplicate] }));

        catalog.FindById(1).Should().Be(new Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses.ClearingHouseDescriptor(1, "Availity"));
        catalog.FindById(2).Should().BeNull();
        catalog.GetRequired(1).Should().BeSameAs(first);
        FluentActions.Invoking(() => catalog.GetRequired(2)).Should().Throw<InvalidOperationException>().WithMessage("Clearing house 2 is not configured.");
        FluentActions.Invoking(() => new ConfiguredClearingHouseCatalog(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Credential_provider_resolves_every_setting_with_the_equals_rule()
    {
        var settings = Complete();
        settings.HostKeyFingerprint = "availity-hostkey";
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? value, string _, ISecretReferencePolicy _, CancellationToken _) => value switch
            {
                "availity-hosts" => "sftp.example.test",
                "availity-port" => "9922",
                "availity-username" => "rethink",
                "availity-password" => "p@ss",
                "availity-downloadDirectory" => "/ReceiveFiles/",
                "availity-hostkey" => "SHA256:pinned",
                _ => throw new InvalidOperationException(value),
            });
        var provider = new ClearingHouseSftpCredentialProvider(Source(settings), resolver.Object);

        var credentials = await provider.GetAsync(1, CancellationToken.None);

        credentials.Host.Should().Be("sftp.example.test");
        credentials.Port.Should().Be(9922);
        credentials.UserName.Should().Be("rethink");
        credentials.Password.Should().Be("p@ss");
        credentials.DownloadDirectory.Should().Be("/ReceiveFiles/");
        credentials.HostKeyFingerprint.Should().Be("SHA256:pinned");
        credentials.ToString().Should().Be("sftp.example.test:9922 (/ReceiveFiles/)").And.NotContain("p@ss").And.NotContain("rethink");
        resolver.Verify(r => r.ResolveAsync("availity-hosts", "Clearinghouses:Availity:Hosts", LiteralWhenContainsEqualsPolicy.Instance, It.IsAny<CancellationToken>()), Times.Once);
        resolver.Verify(r => r.ResolveAsync("availity-hostkey", "Clearinghouses:Availity:HostKeyFingerprint", SecretNamePatternPolicy.Instance, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Credential_provider_skips_host_key_when_not_configured()
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? value, string _, ISecretReferencePolicy _, CancellationToken _) => value == "availity-port" ? "22" : value!);

        var credentials = await new ClearingHouseSftpCredentialProvider(Source(Complete()), resolver.Object).GetAsync(1, CancellationToken.None);

        credentials.HostKeyFingerprint.Should().BeNull();
        resolver.Verify(r => r.ResolveAsync(It.IsAny<string?>(), It.Is<string>(name => name.EndsWith("HostKeyFingerprint")), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Credential_provider_rejects_invalid_ports(string port)
    {
        var resolver = new Mock<ISecretResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<ISecretReferencePolicy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? value, string _, ISecretReferencePolicy _, CancellationToken _) => value == "availity-port" ? port : value!);

        var act = () => new ClearingHouseSftpCredentialProvider(Source(Complete()), resolver.Object).GetAsync(1, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Clearinghouses:Availity:Port must be a positive number.");
    }

    private static ClearingHouseSettings Complete() => new()
    {
        Id = 1,
        Title = "Availity",
        SectionKey = "Availity",
        Hosts = "availity-hosts",
        Port = "availity-port",
        UserName = "availity-username",
        UserPassword = "availity-password",
        DownloadDirectory = "availity-downloadDirectory",
    };

    private static ConfiguredClearingHouseCatalog Source(ClearingHouseSettings settings)
        => new ConfiguredClearingHouseCatalog(Microsoft.Extensions.Options.Options.Create(new ClearingHouseCatalogOptions { ClearingHouses = [settings] }));

    private static IConfiguration Build(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
