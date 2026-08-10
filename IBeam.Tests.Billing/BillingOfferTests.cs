using IBeam.Billing;
using IBeam.Billing.Services;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Billing;

[TestClass]
public sealed class BillingOfferTests
{
    [TestMethod]
    public void IndividualOffer_DefaultsToOneLicenseAndOneTotalSeat()
    {
        var offer = CreateOffer(
            "individual-monthly",
            BillingOfferSeatPolicyInfo.Create(defaultTotalSeats: 1, minimumTotalSeats: 1),
            BillingOfferPricingInfo.Create(baseAmount: 10m, perSeatAmount: 5m));

        var initial = offer.Quote();
        var expanded = offer.Quote(requestedTotalSeats: 4);

        Assert.AreEqual(1, initial.LicenseQuantity);
        Assert.AreEqual(1, initial.TotalSeats);
        Assert.AreEqual(15m, initial.TotalAmount);
        Assert.AreEqual(1, expanded.LicenseQuantity);
        Assert.AreEqual(4, expanded.TotalSeats);
        Assert.AreEqual(30m, expanded.TotalAmount);
    }

    [TestMethod]
    public void TeamOffer_UsesThreeAsMinimumTotalSeatQuantity()
    {
        var offer = CreateOffer(
            "team-monthly",
            BillingOfferSeatPolicyInfo.Create(defaultTotalSeats: 3, minimumTotalSeats: 3),
            BillingOfferPricingInfo.Create(perSeatAmount: 20m));

        var quote = offer.Quote(3);
        var exception = Assert.ThrowsExactly<BillingException>(() => offer.Quote(2));

        Assert.AreEqual(1, quote.LicenseQuantity);
        Assert.AreEqual(3, quote.TotalSeats);
        Assert.AreEqual(60m, quote.TotalAmount);
        StringAssert.Contains(exception.Message, "at least 3");
    }

    [TestMethod]
    public void SeatPolicy_EnforcesMaximumAndIncrementFromMinimum()
    {
        var policy = BillingOfferSeatPolicyInfo.Create(
            defaultTotalSeats: 3,
            minimumTotalSeats: 3,
            maximumTotalSeats: 9,
            seatIncrement: 2);

        Assert.AreEqual(5, policy.NormalizeTotalSeats(5));
        Assert.ThrowsExactly<BillingException>(() => policy.NormalizeTotalSeats(4));
        Assert.ThrowsExactly<BillingException>(() => policy.NormalizeTotalSeats(11));
    }

    [TestMethod]
    public void GraduatedPricing_ChargesSeatsAcrossTiers()
    {
        var pricing = BillingOfferPricingInfo.Create(
            baseAmount: 10m,
            tiers:
            [
                new BillingOfferPriceTierInfo(3, 20m),
                new BillingOfferPriceTierInfo(10, 15m),
                new BillingOfferPriceTierInfo(null, 10m)
            ]);

        Assert.AreEqual(100m, pricing.CalculateTotal(5));
        Assert.AreEqual(195m, pricing.CalculateTotal(12));
    }

    [TestMethod]
    public void Pricing_RejectsInvalidAndUncoveredQuantities()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => BillingOfferPricingInfo.Create(perSeatAmount: -1m));
        Assert.ThrowsExactly<ArgumentException>(
            () => BillingOfferPricingInfo.Create(
                tiers:
                [
                    new BillingOfferPriceTierInfo(10, 10m),
                    new BillingOfferPriceTierInfo(5, 5m)
                ]));

        var pricing = BillingOfferPricingInfo.Create(
            tiers: [new BillingOfferPriceTierInfo(3, 20m)]);

        Assert.ThrowsExactly<BillingException>(() => pricing.CalculateTotal(4));
    }

    [TestMethod]
    public async Task ConfigurationCatalog_NormalizesOfferAndProviderPrices()
    {
        var provider = new ConfigurationBillingOfferCatalogProvider(
            Options.Create(new BillingOptions
            {
                Offers =
                [
                    new BillingOfferOptions
                    {
                        Key = " team-monthly ",
                        ProductKey = " hubbsly ",
                        PlanKey = " hubbsly-pro ",
                        DisplayName = " Hubbsly Pro ",
                        BillingPeriod = " MONTHLY ",
                        Currency = " usd ",
                        SeatPolicy = new BillingOfferSeatPolicyOptions
                        {
                            DefaultTotalSeats = 3,
                            MinimumTotalSeats = 3,
                            SeatIncrement = 1
                        },
                        Pricing = new BillingOfferPricingOptions { PerSeatAmount = 25m },
                        ProviderPrices =
                        [
                            new BillingOfferProviderPriceOptions
                            {
                                ProviderName = " stripe ",
                                PriceId = " price_pro_monthly ",
                                BillingMode = " self_service_monthly "
                            }
                        ],
                        Metadata = new Dictionary<string, string> { [" audience "] = " teams " }
                    }
                ]
            }));

        var offers = await provider.ListOffersAsync();
        var offer = await provider.GetOfferAsync(" TEAM-MONTHLY ");

        Assert.HasCount(1, offers);
        Assert.IsNotNull(offer);
        Assert.AreEqual("team-monthly", offer.Key);
        Assert.AreEqual("hubbsly", offer.ProductKey);
        Assert.AreEqual("hubbsly-pro", offer.PlanKey);
        Assert.AreEqual("monthly", offer.BillingPeriod);
        Assert.AreEqual("USD", offer.Currency);
        Assert.AreEqual("teams", offer.Metadata["audience"]);
        Assert.HasCount(1, offer.ProviderPrices);
        Assert.AreEqual("stripe", offer.ProviderPrices[0].ProviderName);
        Assert.AreEqual("price_pro_monthly", offer.ProviderPrices[0].PriceId);
        Assert.AreEqual(BillingModes.SelfServiceMonthly, offer.ProviderPrices[0].BillingMode);
        Assert.AreEqual(75m, offer.Quote(3).TotalAmount);
    }

    [TestMethod]
    public void ConfigurationCatalog_RejectsDuplicateOfferKeys()
    {
        var options = Options.Create(new BillingOptions
        {
            Offers =
            [
                CreateOfferOptions("team"),
                CreateOfferOptions(" TEAM ")
            ]
        });

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new ConfigurationBillingOfferCatalogProvider(options));

        StringAssert.Contains(exception.Message, "configured more than once");
    }

    private static BillingOfferInfo CreateOffer(
        string key,
        BillingOfferSeatPolicyInfo seatPolicy,
        BillingOfferPricingInfo pricing)
        => BillingOfferInfo.Create(
            key,
            "hubbsly",
            "hubbsly-pro",
            "Hubbsly Pro",
            null,
            "monthly",
            "usd",
            seatPolicy,
            pricing);

    private static BillingOfferOptions CreateOfferOptions(string key)
        => new()
        {
            Key = key,
            ProductKey = "hubbsly",
            PlanKey = "hubbsly-pro",
            DisplayName = "Hubbsly Pro",
            BillingPeriod = "monthly",
            Currency = "USD",
            SeatPolicy = new BillingOfferSeatPolicyOptions
            {
                DefaultTotalSeats = 3,
                MinimumTotalSeats = 3
            },
            Pricing = new BillingOfferPricingOptions { PerSeatAmount = 20m }
        };
}
