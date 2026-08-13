using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public static class InventoryIdempotencyKeyFactory
{
    public const byte Version = 1;
    public const int KeyLength = 32;

    public static byte[] Create(
        int storeId,
        CreateInventoryMovementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(
            storeId,
            request.WarehouseId,
            request.ProductVariantId,
            request.ReferenceId,
            request.ReferenceSubKey);

        return Create(
            storeId,
            request.WarehouseId,
            request.ProductVariantId,
            request.TransactionType,
            request.ReferenceType,
            request.ReferenceId!,
            request.ReferenceLineId,
            request.ReferenceSubKey);
    }

    public static byte[] Create(
        int storeId,
        int warehouseId,
        int productVariantId,
        InventoryTransactionType transactionType,
        InventoryReferenceType referenceType,
        string referenceId,
        int? referenceLineId,
        string? referenceSubKey)
    {
        Validate(
            storeId,
            warehouseId,
            productVariantId,
            referenceId,
            referenceSubKey);

        using var canonical = new MemoryStream();
        canonical.WriteByte(Version);
        WriteInt32(canonical, storeId);
        WriteInt32(canonical, warehouseId);
        WriteInt32(canonical, productVariantId);
        WriteInt32(canonical, (int)transactionType);
        WriteInt32(canonical, (int)referenceType);
        WriteRequiredString(canonical, referenceId);
        WriteNullableInt32(canonical, referenceLineId);
        WriteNullableString(canonical, referenceSubKey);

        return SHA256.HashData(canonical.GetBuffer().AsSpan(
            0,
            checked((int)canonical.Length)));
    }

    public static void Validate(
        int storeId,
        int warehouseId,
        int productVariantId,
        string? referenceId,
        string? referenceSubKey)
    {
        if (storeId <= 0)
        {
            throw new BusinessRuleException(
                "StoreId của movement phải lớn hơn 0.");
        }

        if (warehouseId <= 0)
        {
            throw new BusinessRuleException(
                "WarehouseId của movement phải lớn hơn 0.");
        }

        if (productVariantId <= 0)
        {
            throw new BusinessRuleException(
                "ProductVariantId của movement phải lớn hơn 0.");
        }

        ValidateReferenceId(referenceId);
        ValidateReferenceSubKey(referenceSubKey);
    }

    public static bool HasSameCanonicalIdentity(
        InventoryTransaction transaction,
        int storeId,
        CreateInventoryMovementRequest request)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);

        Validate(
            transaction.StoreId,
            transaction.WarehouseId,
            transaction.ProductVariantId,
            transaction.ReferenceId,
            transaction.ReferenceSubKey);
        Validate(
            storeId,
            request.WarehouseId,
            request.ProductVariantId,
            request.ReferenceId,
            request.ReferenceSubKey);

        return transaction.StoreId == storeId
            && transaction.WarehouseId == request.WarehouseId
            && transaction.ProductVariantId == request.ProductVariantId
            && transaction.TransactionType == request.TransactionType
            && transaction.ReferenceType == request.ReferenceType
            && string.Equals(
                Normalize(transaction.ReferenceId!),
                Normalize(request.ReferenceId!),
                StringComparison.Ordinal)
            && transaction.ReferenceLineId == request.ReferenceLineId
            && NullableCanonicalStringEquals(
                transaction.ReferenceSubKey,
                request.ReferenceSubKey);
    }

    private static void ValidateReferenceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleException(
                "ReferenceId là bắt buộc cho durable inventory posting.");
        }

        if (value.Length > 64)
        {
            throw new BusinessRuleException(
                "ReferenceId không được vượt quá 64 ký tự.");
        }

        if (HasBoundaryWhitespace(value))
        {
            throw new BusinessRuleException(
                "ReferenceId không được có khoảng trắng ở đầu hoặc cuối.");
        }
    }

    private static void ValidateReferenceSubKey(string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > 100)
        {
            throw new BusinessRuleException(
                "ReferenceSubKey không được vượt quá 100 ký tự.");
        }

        if (HasBoundaryWhitespace(value))
        {
            throw new BusinessRuleException(
                "ReferenceSubKey không được có khoảng trắng ở đầu hoặc cuối.");
        }
    }

    private static bool HasBoundaryWhitespace(string value)
        => value.Length > 0
            && (char.IsWhiteSpace(value[0])
                || char.IsWhiteSpace(value[^1]));

    private static string Normalize(string value)
        => value.Normalize(NormalizationForm.FormC);

    private static bool NullableCanonicalStringEquals(
        string? left,
        string? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.Ordinal);
    }

    private static void WriteInt32(Stream destination, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        destination.Write(buffer);
    }

    private static void WriteRequiredString(
        Stream destination,
        string value)
    {
        var bytes = Encoding.UTF8.GetBytes(Normalize(value));
        WriteInt32(destination, bytes.Length);
        destination.Write(bytes);
    }

    private static void WriteNullableInt32(
        Stream destination,
        int? value)
    {
        destination.WriteByte(value.HasValue ? (byte)1 : (byte)0);
        if (value.HasValue)
        {
            WriteInt32(destination, value.Value);
        }
    }

    private static void WriteNullableString(
        Stream destination,
        string? value)
    {
        destination.WriteByte(value is null ? (byte)0 : (byte)1);
        if (value is not null)
        {
            WriteRequiredString(destination, value);
        }
    }
}
