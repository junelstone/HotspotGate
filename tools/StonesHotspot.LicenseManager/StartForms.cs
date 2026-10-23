using StonesHotspot.LicenseIssuer;
using static StonesHotspot.LicenseManager.Ui;

namespace StonesHotspot.LicenseManager;

/// <summary>
/// First run: choose the password, create the keys, and save an encrypted backup of the key file before anything
/// else (on a USB stick, not on this PC). Without the key file and its password, no licence can ever be signed again.
/// </summary>
internal static class FirstRun
{
    public static OwnerKeys? Show()
    {
        var column = Column();
        column.Controls.Add(Label(T("Bienvenue. Cet outil crée les licences de Stone's Hotspot. Il contient votre clé privée : ne l'installez jamais chez un client.",
            "Welcome. This tool creates Stone's Hotspot licences. It holds your private key: never install it at a customer's."), bold: true));
        column.Controls.Add(Label(T(
            "Choisissez un mot de passe d'au moins 12 caractères. Il chiffre la clé privée. Si vous perdez le mot de passe ET le fichier de clés, plus aucune licence ne pourra être créée ni renouvelée. Si quelqu'un obtient les deux, il peut créer des licences : gardez-les séparés.",
            "Choose a password of at least 12 characters. It encrypts the private key. If you lose the password AND the key file, no licence can ever be created or renewed again. Anyone who gets both can create licences: keep them apart.")));
        var password = Box(password: true);
        var confirm = Box(password: true);
        column.Controls.Add(Label(T("Mot de passe", "Password")));
        column.Controls.Add(password);
        column.Controls.Add(Label(T("Confirmez le mot de passe", "Confirm the password")));
        column.Controls.Add(confirm);

        OwnerKeys? created = null;
        using var form = Dialog("Stone's Hotspot — LicenseManager", column);
        column.Controls.Add(Button(T("Créer les clés", "Create the keys"), (_, _) =>
        {
            if (password.Text.Length < KeyVault.MinPasswordLength)
            {
                Error(form, T($"Au moins {KeyVault.MinPasswordLength} caractères.", $"At least {KeyVault.MinPasswordLength} characters."));
                return;
            }
            if (password.Text != confirm.Text)
            {
                Error(form, T("Les deux mots de passe sont différents.", "The two passwords differ."));
                return;
            }
            Directory.CreateDirectory(ManagerFiles.Folder);
            var keys = KeyVault.Generate();
            KeyVault.Save(keys, password.Text, ManagerFiles.KeyFile);
            if (!ForceBackup(form))
            {
                File.Delete(ManagerFiles.KeyFile);
                return;
            }
            created = keys;
            form.DialogResult = DialogResult.OK;
        }));
        return form.ShowDialog() == DialogResult.OK ? created : null;
    }

    /// <summary>Asks where to save a copy of the key file and does not give up until it is saved or the owner cancels everything.</summary>
    public static bool ForceBackup(IWin32Window owner)
    {
        Info(owner, T("Enregistrez maintenant une copie du fichier de clés, de préférence sur une clé USB que vous gardez en lieu sûr.",
            "Now save a copy of the key file, preferably on a USB stick kept in a safe place."));
        while (true)
        {
            using var dialog = new SaveFileDialog { FileName = $"stoneshotspot-keys-{DateTime.Now:yyyyMMdd}.shkey", Filter = "Clés (*.shkey)|*.shkey" };
            if (dialog.ShowDialog(owner) == DialogResult.OK)
            {
                File.Copy(ManagerFiles.KeyFile, dialog.FileName, overwrite: true);
                Info(owner, T($"Copie enregistrée : {dialog.FileName}", $"Copy saved: {dialog.FileName}"));
                return true;
            }
            if (Confirm(owner, T("Sans copie, une panne de ce PC fait perdre la clé pour toujours. Annuler la création des clés ?",
                    "Without a copy, a broken PC loses the key forever. Cancel creating the keys?")))
                return false;
        }
    }
}

/// <summary>Password prompt; slows down after wrong passwords.</summary>
internal static class SignIn
{
    public static OwnerKeys? Show()
    {
        var column = Column();
        column.Controls.Add(Label("Stone's Hotspot — LicenseManager", bold: true));
        column.Controls.Add(Label(T("Mot de passe", "Password")));
        var password = Box(password: true);
        column.Controls.Add(password);
        OwnerKeys? keys = null;
        var failures = 0;
        using var form = Dialog("LicenseManager", column, 420);
        var open = Button(T("Ouvrir", "Open"), (_, _) =>
        {
            try
            {
                keys = KeyVault.Open(ManagerFiles.KeyFile, password.Text);
                form.DialogResult = DialogResult.OK;
            }
            catch (WrongPasswordException)
            {
                failures++;
                password.Clear();
                if (failures >= 3)
                    Thread.Sleep(TimeSpan.FromSeconds(Math.Min(30, 5 * failures)));
                Error(form, T("Mot de passe incorrect.", "Wrong password."));
            }
        });
        column.Controls.Add(open);
        form.AcceptButton = open;
        return form.ShowDialog() == DialogResult.OK ? keys : null;
    }
}
