using System.Security.Cryptography;

namespace LumoraGo.App.Services;

/// <summary>Gizli bilgileri Windows kullanıcı hesabına bağlı DPAPI ile korur; dosya başka hesapta okunamaz.</summary>
internal static class SecretProtection
{
    public static byte[] Protect(byte[] data) => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
}
