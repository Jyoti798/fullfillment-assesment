using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Exceptions;
using Fulfillment.UnitTests.Support;

namespace Fulfillment.UnitTests.Domain;

public class ProductTests
{
    [Fact]
    public void Constructor_normalises_sku_and_trims_text()
    {
        var product = new Product("  kb-100 ", "  Keyboard ", "  desc ", 10m, 5, 2);

        Assert.Equal("KB-100", product.Sku);
        Assert.Equal("Keyboard", product.Name);
        Assert.Equal("desc", product.Description);
        Assert.True(product.IsActive);
    }

    [Theory]
    [InlineData("", "Name", 1, 0, 0, "Sku")]
    [InlineData("SKU", " ", 1, 0, 0, "Name")]
    [InlineData("SKU", "Name", -1, 0, 0, "UnitPrice")]
    [InlineData("SKU", "Name", 1, -1, 0, "StockQuantity")]
    [InlineData("SKU", "Name", 1, 0, -1, "ReorderThreshold")]
    public void Constructor_rejects_invalid_values(string sku, string name, double price, int stock, int threshold, string field)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            new Product(sku, name, null, (decimal)price, stock, threshold));

        Assert.Contains(field, ex.Errors.Keys);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10")]
    [InlineData("9.9")]
    [InlineData("9.99")]
    [InlineData("0.01")]
    [InlineData("999999999.99")]
    public void Prices_with_at_most_two_decimal_places_are_accepted(string price)
    {
        var product = new Product("SKU", "Name", null, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), 1, 0);

        Assert.Equal(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), product.UnitPrice);
    }

    [Theory]
    [InlineData("9.999")]
    [InlineData("0.001")]
    [InlineData("10.005")]
    public void Prices_finer_than_a_cent_are_rejected_rather_than_silently_stored(string price)
    {
        // The column is declared decimal(18,2), but SQLite stores decimals as text and enforces no precision,
        // so the domain is the only place that can keep totals to whole cents.
        var ex = Assert.Throws<DomainValidationException>(() =>
            new Product("SKU", "Name", null, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), 1, 0));

        Assert.Contains("UnitPrice", ex.Errors.Keys);
        Assert.Contains("two decimal places", ex.Errors["UnitPrice"].Single());
    }

    [Fact]
    public void UpdateDetails_enforces_the_same_price_precision()
    {
        var product = TestData.Product();

        Assert.Throws<DomainValidationException>(() => product.UpdateDetails("Name", null, 1.234m, 1, true));
        Assert.Equal(10m, product.UnitPrice);
    }

    [Theory]
    [InlineData(5, 5, true)]   // exactly at the threshold counts as low
    [InlineData(4, 5, true)]
    [InlineData(6, 5, false)]
    [InlineData(0, 0, true)]
    public void IsLowStock_is_true_at_or_below_threshold(int stock, int threshold, bool expected)
    {
        Assert.Equal(expected, TestData.Product(stock: stock, threshold: threshold).IsLowStock);
    }

    [Fact]
    public void AdjustStock_adds_and_removes()
    {
        var product = TestData.Product(stock: 10);

        product.AdjustStock(5);
        product.AdjustStock(-12);

        Assert.Equal(3, product.StockQuantity);
    }

    [Fact]
    public void AdjustStock_cannot_go_below_zero()
    {
        var product = TestData.Product(stock: 2);

        Assert.Throws<ConflictException>(() => product.AdjustStock(-3));
        Assert.Equal(2, product.StockQuantity);
    }

    [Fact]
    public void AdjustStock_can_drain_stock_to_exactly_zero()
    {
        var product = TestData.Product(stock: 2);

        product.AdjustStock(-2);

        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public void AdjustStock_guards_against_integer_overflow()
    {
        var product = TestData.Product(stock: int.MaxValue);

        Assert.Throws<DomainValidationException>(() => product.AdjustStock(1));
    }

    [Fact]
    public void UpdateDetails_changes_mutable_fields_but_not_sku_or_stock()
    {
        var product = TestData.Product("SKU-9", stock: 7);

        product.UpdateDetails("Renamed", "new", 99m, 4, isActive: false);

        Assert.Equal("Renamed", product.Name);
        Assert.Equal(99m, product.UnitPrice);
        Assert.Equal(4, product.ReorderThreshold);
        Assert.False(product.IsActive);
        Assert.Equal("SKU-9", product.Sku);
        Assert.Equal(7, product.StockQuantity);
    }
}
