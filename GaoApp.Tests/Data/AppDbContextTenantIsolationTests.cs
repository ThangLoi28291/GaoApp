
using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Data;

public class AppDbContextTenantIsolationTests
{
    [Fact]
    public async Task Real_context_filter_should_return_only_current_store_non_deleted_rows()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(
            options,
            tenant => tenant.SetHostAdmin()))
        {
            var activeStoreOne = NewCategory(
                1,
                "S1-ACTIVE",
                "Store 1 active");

            var deletedStoreOne = NewCategory(
                1,
                "S1-DELETED",
                "Store 1 deleted");

            var activeStoreTwo = NewCategory(
                2,
                "S2-ACTIVE",
                "Store 2 active");

            seedContext.Categories.AddRange(
                activeStoreOne,
                deletedStoreOne,
                activeStoreTwo);

            await seedContext.SaveChangesAsync();

            seedContext.Categories.Remove(deletedStoreOne);
            await seedContext.SaveChangesAsync();
        }

        await using var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var visibleCodes = await storeOneContext.Categories
            .Select(x => x.Code)
            .ToListAsync();

        visibleCodes.Should().Equal("S1-ACTIVE");
    }

    [Fact]
    public async Task Tenant_guard_should_reject_added_entity_for_another_store()
    {
        var options = CreateOptions();

        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        context.Categories.Add(
            NewCategory(
                2,
                "WRONG-STORE",
                "Wrong store"));

        var action = () => context.SaveChangesAsync();

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*không khớp TenantContext=1*");
    }

    [Fact]
    public async Task Tenant_guard_should_assign_current_store_to_added_entity_with_zero_store_id()
    {
        var options = CreateOptions();

        await using (var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var category = NewCategory(
                0,
                "ZERO-STORE",
                "Zero store");

            context.Categories.Add(category);
            await context.SaveChangesAsync();

            category.StoreId.Should().Be(1);
            category.CreatedBy.Should().Be(99);
            category.CreatedAtUtc.Should().NotBe(default(DateTime));
        }

        await using var verifyContext = CreateContext(
            options,
            tenant => tenant.SetHostAdmin());

        var savedCategory = await verifyContext.Categories
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Code == "ZERO-STORE");

        savedCategory.StoreId.Should().Be(1);
    }

    [Fact]
    public async Task Tenant_guard_should_allow_added_entity_for_current_store()
    {
        var options = CreateOptions();

        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var category = NewCategory(
            1,
            "RIGHT-STORE",
            "Right store");

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        category.StoreId.Should().Be(1);
        category.CreatedBy.Should().Be(99);
    }

    [Fact]
    public async Task Tenant_guard_should_allow_valid_tracked_update_but_restore_original_store_id()
    {
        var options = CreateOptions();

        await SeedCategoryAsync(
            options,
            1,
            "S1-TRACKED-UPDATE",
            "Original name");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var category = await storeOneContext.Categories
                .SingleAsync(x => x.Code == "S1-TRACKED-UPDATE");

            category.Name = "Updated name";
            category.StoreId = 2;

            await storeOneContext.SaveChangesAsync();

            category.StoreId.Should().Be(1);
            category.Name.Should().Be("Updated name");
            category.UpdatedBy.Should().Be(99);
        }

        await using var verifyContext = CreateContext(
            options,
            tenant => tenant.SetHostAdmin());

        var savedCategory = await verifyContext.Categories
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Code == "S1-TRACKED-UPDATE");

        savedCategory.StoreId.Should().Be(1);
        savedCategory.Name.Should().Be("Updated name");
    }

    [Fact]
    public async Task Tenant_guard_should_reject_tracked_update_loaded_from_another_store()
    {
        var options = CreateOptions();

        await SeedCategoryAsync(
            options,
            2,
            "S2-TRACKED-UPDATE",
            "Store 2 category");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var category = await storeOneContext.Categories
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Code == "S2-TRACKED-UPDATE");

            category.Name = "Cross-tenant update";

            var action = () => storeOneContext.SaveChangesAsync();

            await action.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("Tenant mismatch UPDATE");
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "S2-TRACKED-UPDATE");

        savedCategory.StoreId.Should().Be(2);
        savedCategory.Name.Should().Be("Store 2 category");
        savedCategory.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_guard_should_reject_tracked_delete_loaded_from_another_store()
    {
        var options = CreateOptions();

        await SeedCategoryAsync(
            options,
            2,
            "S2-TRACKED-DELETE",
            "Store 2 delete target");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var category = await storeOneContext.Categories
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Code == "S2-TRACKED-DELETE");

            storeOneContext.Categories.Remove(category);

            var action = () => storeOneContext.SaveChangesAsync();

            await action.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("Tenant mismatch DELETE");
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "S2-TRACKED-DELETE");

        savedCategory.StoreId.Should().Be(2);
        savedCategory.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_guard_should_reject_detached_update_when_database_row_belongs_to_another_store()
    {
        var options = CreateOptions();

        var seeded = await SeedCategoryAsync(
            options,
            2,
            "S2-DETACHED-UPDATE",
            "Original store 2 name");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var detached = NewDetachedCategory(
                seeded.Id,
                1,
                "S2-DETACHED-UPDATE",
                "Detached attack update",
                seeded.RowVersion);

            storeOneContext.Update(detached);
            SetOriginalRowVersion(
                storeOneContext,
                detached,
                seeded.RowVersion);

            var action = () => storeOneContext.SaveChangesAsync();

            await action.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("Tenant mismatch UPDATE");
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "S2-DETACHED-UPDATE");

        savedCategory.StoreId.Should().Be(2);
        savedCategory.Name.Should().Be("Original store 2 name");
        savedCategory.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_guard_should_reject_detached_attach_when_database_row_belongs_to_another_store()
    {
        var options = CreateOptions();

        var seeded = await SeedCategoryAsync(
            options,
            2,
            "S2-DETACHED-ATTACH",
            "Original attach name");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var detached = NewDetachedCategory(
                seeded.Id,
                1,
                "S2-DETACHED-ATTACH",
                "Original attach name",
                seeded.RowVersion);

            storeOneContext.Attach(detached);
            detached.Name = "Detached attach attack";

            storeOneContext.Entry(detached)
                .Property(x => x.Name)
                .IsModified = true;

            SetOriginalRowVersion(
                storeOneContext,
                detached,
                seeded.RowVersion);

            var action = () => storeOneContext.SaveChangesAsync();

            await action.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("Tenant mismatch UPDATE");
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "S2-DETACHED-ATTACH");

        savedCategory.StoreId.Should().Be(2);
        savedCategory.Name.Should().Be("Original attach name");
        savedCategory.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_guard_should_reject_detached_remove_when_database_row_belongs_to_another_store()
    {
        var options = CreateOptions();

        var seeded = await SeedCategoryAsync(
            options,
            2,
            "S2-DETACHED-REMOVE",
            "Original remove name");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var detached = NewDetachedCategory(
                seeded.Id,
                1,
                "S2-DETACHED-REMOVE",
                "Original remove name",
                seeded.RowVersion);

            storeOneContext.Remove(detached);
            SetOriginalRowVersion(
                storeOneContext,
                detached,
                seeded.RowVersion);

            var action = () => storeOneContext.SaveChangesAsync();

            await action.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("Tenant mismatch DELETE");
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "S2-DETACHED-REMOVE");

        savedCategory.StoreId.Should().Be(2);
        savedCategory.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_guard_should_allow_detached_update_for_own_row_and_restore_database_store_id()
    {
        var options = CreateOptions();

        var seeded = await SeedCategoryAsync(
            options,
            1,
            "S1-DETACHED-UPDATE",
            "Original own row");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var detached = NewDetachedCategory(
                seeded.Id,
                2,
                "S1-DETACHED-UPDATE",
                "Valid detached update",
                seeded.RowVersion);

            storeOneContext.Update(detached);
            SetOriginalRowVersion(
                storeOneContext,
                detached,
                seeded.RowVersion);

            await storeOneContext.SaveChangesAsync();

            detached.StoreId.Should().Be(1);
            detached.Name.Should().Be("Valid detached update");
            detached.UpdatedBy.Should().Be(99);
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "S1-DETACHED-UPDATE");

        savedCategory.StoreId.Should().Be(1);
        savedCategory.Name.Should().Be("Valid detached update");
    }

    [Fact]
    public async Task Tenant_guard_should_soft_delete_own_row_and_keep_store_id()
    {
        var options = CreateOptions();

        await SeedCategoryAsync(
            options,
            1,
            "S1-VALID-DELETE",
            "Own delete target");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var category = await storeOneContext.Categories
                .SingleAsync(x => x.Code == "S1-VALID-DELETE");

            storeOneContext.Remove(category);
            await storeOneContext.SaveChangesAsync();

            category.StoreId.Should().Be(1);
            category.IsDeleted.Should().BeTrue();
            category.DeletedBy.Should().Be(99);
            category.DeletedAtUtc.Should().NotBeNull();
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "S1-VALID-DELETE");

        savedCategory.StoreId.Should().Be(1);
        savedCategory.IsDeleted.Should().BeTrue();
        savedCategory.DeletedBy.Should().Be(99);
        savedCategory.DeletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Tenant_guard_should_reject_tracked_soft_delete_when_rowversion_is_stale()
    {
        var options = CreateOptions();

        var seeded = await SeedCategoryAsync(
            options,
            1,
            "S1-TRACKED-STALE-DELETE",
            "Original tracked stale name");

        await using var contextA = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var staleCategory = await contextA.Categories
            .SingleAsync(x => x.Id == seeded.Id);

        var staleRowVersion = CloneRowVersion(
            staleCategory.RowVersion);
        var currentRowVersion = NewRowVersion(2);

        await UpdateCategoryAndRowVersionAsync(
            options,
            seeded.Id,
            "Updated by context B",
            currentRowVersion);

        staleCategory.RowVersion.Should().Equal(staleRowVersion);
        contextA.Categories.Remove(staleCategory);

        var action = () => contextA.SaveChangesAsync();

        await action.Should()
            .ThrowAsync<DbUpdateConcurrencyException>()
            .WithMessage("*concurrency token*đã thay đổi hoặc không hợp lệ*");

        var savedCategory = await LoadCategoryByIdIgnoringFiltersAsync(
            options,
            seeded.Id);

        savedCategory.IsDeleted.Should().BeFalse();
        savedCategory.Name.Should().Be("Updated by context B");
        savedCategory.RowVersion.Should().Equal(currentRowVersion);
    }

    [Fact]
    public async Task Tenant_guard_should_reject_detached_soft_delete_when_rowversion_is_stale()
    {
        var options = CreateOptions();

        var seeded = await SeedCategoryAsync(
            options,
            1,
            "S1-DETACHED-STALE-DELETE",
            "Original detached stale name");

        var staleRowVersion = CloneRowVersion(
            seeded.RowVersion);
        var currentRowVersion = NewRowVersion(3);

        await UpdateCategoryAndRowVersionAsync(
            options,
            seeded.Id,
            "Updated before detached delete",
            currentRowVersion);

        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var detached = NewDetachedCategory(
            seeded.Id,
            1,
            "CLIENT-STALE-CODE",
            "Client stale delete name",
            staleRowVersion);

        context.Remove(detached);
        SetOriginalRowVersion(
            context,
            detached,
            staleRowVersion);

        var action = () => context.SaveChangesAsync();

        await action.Should()
            .ThrowAsync<DbUpdateConcurrencyException>()
            .WithMessage("*concurrency token*đã thay đổi hoặc không hợp lệ*");

        var savedCategory = await LoadCategoryByIdIgnoringFiltersAsync(
            options,
            seeded.Id);

        savedCategory.IsDeleted.Should().BeFalse();
        savedCategory.Name.Should().Be("Updated before detached delete");
        savedCategory.Code.Should().Be("S1-DETACHED-STALE-DELETE");
        savedCategory.RowVersion.Should().Equal(currentRowVersion);
    }

    [Fact]
    public async Task Tenant_guard_should_allow_detached_remove_for_own_row_without_overwriting_business_fields()
    {
        var options = CreateOptions();

        var seeded = await SeedCategoryAsync(
            options,
            1,
            "S1-DETACHED-REMOVE",
            "Original detached remove name");

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            // Client gửi entity detached có đúng Id nhưng cố tình khai sai
            // StoreId và thay đổi các field nghiệp vụ.
            var detached = NewDetachedCategory(
                seeded.Id,
                2,
                "CLIENT-OVERWRITE-CODE",
                "Client overwrite name",
                seeded.RowVersion);

            detached.IsActive = false;

            storeOneContext.Remove(detached);
            SetOriginalRowVersion(
                storeOneContext,
                detached,
                seeded.RowVersion);

            await storeOneContext.SaveChangesAsync();

            // Entity đang track phải được khôi phục từ database trước soft delete.
            detached.StoreId.Should().Be(1);
            detached.Code.Should().Be("S1-DETACHED-REMOVE");
            detached.Name.Should().Be("Original detached remove name");
            detached.IsActive.Should().BeTrue();
            detached.IsDeleted.Should().BeTrue();
            detached.DeletedBy.Should().Be(99);
            detached.DeletedAtUtc.Should().NotBeNull();
        }

        var savedCategory = await LoadCategoryByIdIgnoringFiltersAsync(
            options,
            seeded.Id);

        savedCategory.StoreId.Should().Be(1);
        savedCategory.Code.Should().Be("S1-DETACHED-REMOVE");
        savedCategory.Name.Should().Be("Original detached remove name");
        savedCategory.IsActive.Should().BeTrue();
        savedCategory.IsDeleted.Should().BeTrue();
        savedCategory.DeletedBy.Should().Be(99);
        savedCategory.DeletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void SaveChanges_with_accept_all_changes_false_should_still_run_tenant_guard()
    {
        var options = CreateOptions();

        using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        context.Categories.Add(
            NewCategory(
                2,
                "SYNC-BOOL-BYPASS",
                "Sync bool bypass"));

        var action = () => context.SaveChanges(
            acceptAllChangesOnSuccess: false);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*không khớp TenantContext=1*");
    }

    [Fact]
    public async Task SaveChangesAsync_with_accept_all_changes_false_should_still_run_tenant_guard()
    {
        var options = CreateOptions();

        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        context.Categories.Add(
            NewCategory(
                2,
                "ASYNC-BOOL-BYPASS",
                "Async bool bypass"));

        var action = () => context.SaveChangesAsync(
            acceptAllChangesOnSuccess: false,
            cancellationToken: CancellationToken.None);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*không khớp TenantContext=1*");
    }

    [Fact]
    public async Task SaveChanges_with_accept_all_changes_false_should_save_valid_entity_and_preserve_tracker_state()
    {
        var options = CreateOptions();

        using (var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var category = NewCategory(
                0,
                "SYNC-BOOL-VALID",
                "Sync bool valid");

            context.Categories.Add(category);

            var affectedRows = context.SaveChanges(
                acceptAllChangesOnSuccess: false);

            affectedRows.Should().Be(1);
            category.StoreId.Should().Be(1);
            category.CreatedBy.Should().Be(99);
            category.CreatedAtUtc.Should().NotBe(default(DateTime));

            // acceptAllChangesOnSuccess=false phải được truyền xuống EF Core.
            context.Entry(category)
                .State.Should().Be(EntityState.Added);
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "SYNC-BOOL-VALID");

        savedCategory.StoreId.Should().Be(1);
        savedCategory.Name.Should().Be("Sync bool valid");
    }

    [Fact]
    public async Task SaveChangesAsync_with_accept_all_changes_false_should_save_valid_entity_and_preserve_tracker_state()
    {
        var options = CreateOptions();

        await using (var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var category = NewCategory(
                0,
                "ASYNC-BOOL-VALID",
                "Async bool valid");

            context.Categories.Add(category);

            var affectedRows = await context.SaveChangesAsync(
                acceptAllChangesOnSuccess: false,
                cancellationToken: CancellationToken.None);

            affectedRows.Should().Be(1);
            category.StoreId.Should().Be(1);
            category.CreatedBy.Should().Be(99);
            category.CreatedAtUtc.Should().NotBe(default(DateTime));

            // acceptAllChangesOnSuccess=false phải được truyền xuống EF Core.
            context.Entry(category)
                .State.Should().Be(EntityState.Added);
        }

        var savedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "ASYNC-BOOL-VALID");

        savedCategory.StoreId.Should().Be(1);
        savedCategory.Name.Should().Be("Async bool valid");
    }

    [Fact]
    public async Task Host_admin_context_should_allow_add_update_and_delete_across_stores()
    {
        var options = CreateOptions();

        await using (var hostAdminContext = CreateContext(
            options,
            tenant => tenant.SetHostAdmin()))
        {
            var categoryToUpdate = NewCategory(
                2,
                "HOST-ADMIN-UPDATE",
                "Host original update");

            var categoryToDelete = NewCategory(
                3,
                "HOST-ADMIN-DELETE",
                "Host delete target");

            hostAdminContext.Categories.AddRange(
                categoryToUpdate,
                categoryToDelete);

            await hostAdminContext.SaveChangesAsync();

            categoryToUpdate.Name = "Updated by host admin";
            hostAdminContext.Categories.Remove(categoryToDelete);

            await hostAdminContext.SaveChangesAsync();
        }

        var updatedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "HOST-ADMIN-UPDATE");

        var deletedCategory = await LoadCategoryIgnoringFiltersAsync(
            options,
            "HOST-ADMIN-DELETE");

        updatedCategory.StoreId.Should().Be(2);
        updatedCategory.Name.Should().Be("Updated by host admin");

        deletedCategory.StoreId.Should().Be(3);
        deletedCategory.IsDeleted.Should().BeTrue();
        deletedCategory.DeletedBy.Should().Be(99);
    }

    [Fact]
    public async Task Product_variant_attribute_value_should_be_hard_deleted_instead_of_soft_deleted()
    {
        var options = CreateOptions();

        await using (var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var mapping = new ProductVariantAttributeValue
            {
                StoreId = 1,
                VariantId = 101,
                AttributeId = 201,
                AttributeValueId = 301,
                RowVersion = new byte[8]
            };

            context.ProductVariantAttributeValues.Add(mapping);
            await context.SaveChangesAsync();

            context.ProductVariantAttributeValues.Remove(mapping);
            await context.SaveChangesAsync();

            context.Entry(mapping)
                .State.Should().Be(EntityState.Detached);
        }

        await using var verifyContext = CreateContext(
            options,
            tenant => tenant.SetHostAdmin());

        var mappingStillExists = await verifyContext
            .ProductVariantAttributeValues
            .IgnoreQueryFilters()
            .AnyAsync(x =>
                x.StoreId == 1 &&
                x.VariantId == 101 &&
                x.AttributeId == 201 &&
                x.AttributeValueId == 301);

        mappingStillExists.Should().BeFalse();
    }

    [Fact]
    public async Task Tenant_guard_should_reject_cross_tenant_product_variant_attribute_value_hard_delete()
    {
        var options = CreateOptions();

        await using (var seedContext = CreateContext(
            options,
            tenant => tenant.SetHostAdmin()))
        {
            seedContext.ProductVariantAttributeValues.Add(
                new ProductVariantAttributeValue
                {
                    StoreId = 2,
                    VariantId = 102,
                    AttributeId = 202,
                    AttributeValueId = 302,
                    RowVersion = NewRowVersion(4)
                });

            await seedContext.SaveChangesAsync();
        }

        await using (var storeOneContext = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one")))
        {
            var mapping = await storeOneContext
                .ProductVariantAttributeValues
                .IgnoreQueryFilters()
                .SingleAsync(x =>
                    x.StoreId == 2 &&
                    x.VariantId == 102 &&
                    x.AttributeId == 202 &&
                    x.AttributeValueId == 302);

            storeOneContext.ProductVariantAttributeValues.Remove(mapping);

            var action = () => storeOneContext.SaveChangesAsync();

            await action.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("Tenant mismatch DELETE");
        }

        await using var verifyContext = CreateContext(
            options,
            tenant => tenant.SetHostAdmin());

        var mappingStillExists = await verifyContext
            .ProductVariantAttributeValues
            .IgnoreQueryFilters()
            .AnyAsync(x =>
                x.StoreId == 2 &&
                x.VariantId == 102 &&
                x.AttributeId == 202 &&
                x.AttributeValueId == 302);

        mappingStillExists.Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_guard_should_throw_concurrency_exception_when_detached_row_no_longer_exists()
    {
        var options = CreateOptions();

        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var detached = NewDetachedCategory(
            999_999,
            1,
            "MISSING-ROW",
            "Missing row",
            new byte[8]);

        context.Update(detached);

        var action = () => context.SaveChangesAsync();

        await action.Should()
            .ThrowAsync<DbUpdateConcurrencyException>()
            .WithMessage("*dữ liệu không còn tồn tại trong database*");
    }

    [Fact]
    public async Task Tenant_guard_should_not_partially_mutate_batch_before_all_entries_are_verified()
    {
        var options = CreateOptions();

        await SeedCategoryAsync(
            options,
            2,
            "S2-BATCH-BLOCKER",
            "Batch blocker");

        await using var context = CreateContext(
            options,
            tenant => tenant.SetStore(1, "store-one"));

        var validAddedCategory = NewCategory(
            0,
            "S1-BATCH-ADD",
            "Pending valid add");

        context.Categories.Add(validAddedCategory);

        var crossTenantCategory = await context.Categories
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Code == "S2-BATCH-BLOCKER");

        crossTenantCategory.Name = "Cross tenant batch update";

        var createdAtBeforeSave = validAddedCategory.CreatedAtUtc;
        var createdByBeforeSave = validAddedCategory.CreatedBy;

        var action = () => context.SaveChangesAsync();

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Tenant mismatch UPDATE");

        validAddedCategory.StoreId.Should().Be(0);
        validAddedCategory.CreatedAtUtc.Should().Be(createdAtBeforeSave);
        validAddedCategory.CreatedBy.Should().Be(createdByBeforeSave);

        context.Entry(validAddedCategory)
            .State.Should().Be(EntityState.Added);
    }

    private static DbContextOptions<InMemoryAppDbContext> CreateOptions()
        => new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static AppDbContext CreateContext(
        DbContextOptions<InMemoryAppDbContext> options,
        Action<TenantContext> configureTenant)
    {
        var tenant = new TenantContext();
        configureTenant(tenant);

        var context = new InMemoryAppDbContext(
            options,
            tenant,
            new TestCurrentUser());

        context.VerifyRowVersionConfiguration();

        return context;
    }

    private static async Task<(int Id, byte[] RowVersion)> SeedCategoryAsync(
        DbContextOptions<InMemoryAppDbContext> options,
        int storeId,
        string code,
        string name)
    {
        await using var context = CreateContext(
            options,
            tenant => tenant.SetHostAdmin());

        var category = NewCategory(
            storeId,
            code,
            name);

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return (
            category.Id,
            CloneRowVersion(category.RowVersion));
    }

    private static async Task UpdateCategoryAndRowVersionAsync(
        DbContextOptions<InMemoryAppDbContext> options,
        int id,
        string name,
        byte[] rowVersion)
    {
        await using var context = CreateContext(
            options,
            tenant => tenant.SetHostAdmin());

        var category = await context.Categories
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == id);

        category.Name = name;
        category.RowVersion = CloneRowVersion(rowVersion);

        context.Entry(category)
            .Property(x => x.RowVersion)
            .IsModified = true;

        await context.SaveChangesAsync();

        // EF InMemory không tự sinh rowversion như SQL Server.
        // Xác nhận test store đã nhận token mới trước khi mô phỏng stale delete.
        var persistedValues = await context.Entry(category)
            .GetDatabaseValuesAsync();

        persistedValues.Should().NotBeNull();

        if (persistedValues is null)
        {
            throw new InvalidOperationException(
                "Không đọc được database values sau khi cập nhật rowversion test.");
        }

        persistedValues.GetValue<byte[]>(nameof(Category.RowVersion))
            .Should().Equal(rowVersion);
    }

    private static async Task<Category> LoadCategoryIgnoringFiltersAsync(
        DbContextOptions<InMemoryAppDbContext> options,
        string code)
    {
        await using var context = CreateContext(
            options,
            tenant => tenant.SetHostAdmin());

        return await context.Categories
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Code == code);
    }

    private static async Task<Category> LoadCategoryByIdIgnoringFiltersAsync(
        DbContextOptions<InMemoryAppDbContext> options,
        int id)
    {
        await using var context = CreateContext(
            options,
            tenant => tenant.SetHostAdmin());

        return await context.Categories
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == id);
    }

    private static Category NewCategory(
        int storeId,
        string code,
        string name)
        => new()
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            IsActive = true,

            // SQL Server sinh rowversion tự động.
            // EF InMemory không sinh nên test tự cấp 8 byte.
            RowVersion = new byte[8]
        };

    private static Category NewDetachedCategory(
        int id,
        int storeId,
        string code,
        string name,
        byte[] rowVersion)
        => new()
        {
            Id = id,
            StoreId = storeId,
            Code = code,
            Name = name,
            IsActive = true,
            RowVersion = CloneRowVersion(rowVersion)
        };

    private static void SetOriginalRowVersion(
        AppDbContext context,
        Category category,
        byte[] rowVersion)
    {
        context.Entry(category)
            .Property(x => x.RowVersion)
            .OriginalValue = CloneRowVersion(rowVersion);
    }

    private static byte[] NewRowVersion(byte marker)
    {
        var rowVersion = new byte[8];
        rowVersion[^1] = marker;
        return rowVersion;
    }

    private static byte[] CloneRowVersion(byte[]? rowVersion)
        => rowVersion is null
            ? new byte[8]
            : rowVersion.ToArray();

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;

        public string? UserName => "phase3-test";

        public int? TerminalId => null;

        public string? TerminalCode => null;

        public bool IsAuthenticated => true;
    }
}