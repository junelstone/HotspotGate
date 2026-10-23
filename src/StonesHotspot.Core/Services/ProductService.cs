using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;

namespace StonesHotspot.Core.Services;

/// <summary>Products sold at the till (drinks, snacks, printing…), their groups and their stock.</summary>
public sealed class ProductService(HotspotDbContext db, AuditService audit)
{
    public const int MaxStock = 1_000_000;

    public Task<List<ProductGroup>> ListGroupsAsync(CancellationToken ct = default) =>
        db.ProductGroups.OrderBy(g => g.Name).ToListAsync(ct);

    public Task<List<Product>> ListAsync(bool includeInactive = false, CancellationToken ct = default) =>
        db.Products.Include(p => p.Group).Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.Group == null ? "" : p.Group.Name).ThenBy(p => p.Name).ToListAsync(ct);

    public Task<Product?> FindAsync(int id, CancellationToken ct = default) =>
        db.Products.Include(p => p.Group).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<ProductGroup> CreateGroupAsync(string name, Staff? actor, CancellationToken ct = default)
    {
        name = await ValidateGroupNameAsync(name, null, ct);
        var group = new ProductGroup { Name = name };
        db.ProductGroups.Add(group);
        audit.Record(actor, "ProductGroup.Created", name);
        await db.SaveChangesAsync(ct);
        return group;
    }

    public async Task RenameGroupAsync(int id, string name, Staff? actor, CancellationToken ct = default)
    {
        var group = await db.ProductGroups.FirstOrDefaultAsync(g => g.Id == id, ct)
                    ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Group {id} not found.");
        name = await ValidateGroupNameAsync(name, id, ct);
        audit.Record(actor, "ProductGroup.Renamed", $"'{group.Name}' → '{name}'");
        group.Name = name;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deletes a group; its products stay, without a group.</summary>
    public async Task DeleteGroupAsync(int id, Staff? actor, CancellationToken ct = default)
    {
        var group = await db.ProductGroups.Include(g => g.Products).FirstOrDefaultAsync(g => g.Id == id, ct)
                    ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Group {id} not found.");
        foreach (var product in group.Products)
            product.GroupId = null;
        db.ProductGroups.Remove(group);
        audit.Record(actor, "ProductGroup.Deleted", group.Name);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Product> CreateAsync(Product product, Staff? actor, CancellationToken ct = default)
    {
        product.Name = product.Name.Trim();
        await ValidateAsync(product, ct);
        db.Products.Add(product);
        audit.Record(actor, "Product.Created", $"{product.Name} ({product.Price}), stock {product.Stock}");
        await db.SaveChangesAsync(ct);
        return product;
    }

    /// <summary>Copies name, group, price, VAT, track-stock and active onto product <paramref name="id"/>. Stock changes go through <see cref="AddStockAsync"/> / <see cref="CountStockAsync"/>.</summary>
    public async Task<Product> UpdateAsync(int id, Product changes, Staff? actor, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Product {id} not found.");
        changes.Id = id;
        changes.Name = changes.Name.Trim();
        changes.Stock = product.Stock;
        await ValidateAsync(changes, ct);

        var priceNote = product.Price == changes.Price ? "" : $", price {product.Price} → {changes.Price}";
        product.Name = changes.Name;
        product.GroupId = changes.GroupId;
        product.Price = changes.Price;
        product.VatRate = changes.VatRate;
        product.TrackStock = changes.TrackStock;
        product.IsActive = changes.IsActive;

        audit.Record(actor, "Product.Updated", product.Name + priceNote);
        await db.SaveChangesAsync(ct);
        return product;
    }

    /// <summary>Deletes a product that was never sold; otherwise refuses (deactivate it instead, sales keep their history).</summary>
    public async Task DeleteAsync(int id, Staff? actor, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Product {id} not found.");
        if (await db.Transactions.AnyAsync(t => t.ProductId == id, ct))
            throw new BusinessRuleException(ErrorCodes.InUse, $"Product {product.Name} has sales; deactivate it instead.");
        db.Products.Remove(product);
        audit.Record(actor, "Product.Deleted", product.Name);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A delivery: adds <paramref name="quantity"/> to the stock.</summary>
    public async Task<Product> AddStockAsync(int id, int quantity, Staff? actor, CancellationToken ct = default)
    {
        if (quantity is < 1 or > MaxStock)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Quantity must be positive.");
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Product {id} not found.");
        var before = product.Stock;
        product.Stock = Math.Min(MaxStock, before + quantity);
        audit.Record(actor, "Product.StockAdded", $"{product.Name}: +{quantity} ({before} → {product.Stock})");
        await db.SaveChangesAsync(ct);
        return product;
    }

    /// <summary>A stock count: sets the stock to what was counted on the shelf.</summary>
    public async Task<Product> CountStockAsync(int id, int counted, Staff? actor, CancellationToken ct = default)
    {
        if (counted is < 0 or > MaxStock)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Counted stock must be 0 or more.");
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new BusinessRuleException(ErrorCodes.NotFound, $"Product {id} not found.");
        var before = product.Stock;
        product.Stock = counted;
        audit.Record(actor, "Product.StockCounted", $"{product.Name}: {before} → {counted} (difference {counted - before:+0;-0;0})");
        await db.SaveChangesAsync(ct);
        return product;
    }

    private async Task ValidateAsync(Product product, CancellationToken ct)
    {
        if (product.Name.Length is 0 or > 100)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Name is required (max 100 characters).");
        if (product.Price.IsNegative)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Price cannot be negative.");
        if (product.VatRate is < 0 or >= 1)
            throw new BusinessRuleException(ErrorCodes.Invalid, "VAT rate must be between 0 and 100 %.");
        if (product.Stock is < 0 or > MaxStock)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Stock must be 0 or more.");
        if (product.GroupId is { } groupId && !await db.ProductGroups.AnyAsync(g => g.Id == groupId, ct))
            throw new BusinessRuleException(ErrorCodes.NotFound, $"Group {groupId} not found.");
        // Names are compared in memory: SQLite's lower() only folds ASCII, and product lists are short.
        var names = await db.Products.Where(p => p.Id != product.Id).Select(p => p.Name).ToListAsync(ct);
        if (names.Any(n => string.Equals(n, product.Name, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"A product named '{product.Name}' already exists.");
    }

    private async Task<string> ValidateGroupNameAsync(string name, int? id, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length is 0 or > 100)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Name is required (max 100 characters).");
        var names = await db.ProductGroups.Where(g => g.Id != id).Select(g => g.Name).ToListAsync(ct);
        if (names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"A group named '{name}' already exists.");
        return name;
    }
}
