using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Data.Entities;

public class ProductGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? GroupId { get; set; }
    public ProductGroup? Group { get; set; }
    public Xaf Price { get; set; }
    public int Stock { get; set; }

    /// <summary>VAT as a fraction, e.g. 0.189 for 18.9 %. Prices include VAT.</summary>
    public decimal VatRate { get; set; }

    public bool IsActive { get; set; } = true;
}
