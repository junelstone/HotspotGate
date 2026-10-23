using System.Text;
using QRCoder;

namespace StonesHotspot.LicenseIssuer;

/// <summary>Ways to hand a licence code to a customer: the message text, a WhatsApp link, a .lic file, a QR code.</summary>
public static class Sharing
{
    public static string Message(IssuedLicence l) =>
        $"Stone's Hotspot — licence {l.Edition} n° {l.Id}\n" +
        $"Valable jusqu'au {l.ExpiresOn:dd/MM/yyyy} pour {l.MachineId}.\n" +
        "Tableau de bord → Licence → collez ce code → Activer :\n\n" + l.Code;

    /// <summary>wa.me link with the message (to the customer's number when known).</summary>
    public static string WhatsAppLink(IssuedLicence l)
    {
        var digits = new string((l.Phone ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 9 && digits.StartsWith('0'))
            digits = "242" + digits;
        return $"https://wa.me/{digits}?text={Uri.EscapeDataString(Message(l))}";
    }

    public static string LicFile(IssuedLicence l) =>
        $"# Stone's Hotspot — licence {l.Id} ({l.Edition}) for {l.CustomerName}, {l.MachineId}, until {l.ExpiresOn:yyyy-MM-dd}\n{l.Code}\n";

    public static byte[] QrPng(IssuedLicence l)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(l.Code, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(6);
    }
}
