using StonesHotspot.LicenseIssuer;
using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Codes;
using static StonesHotspot.LicenseManager.Ui;

namespace StonesHotspot.LicenseManager;

/// <summary>Licences, customers, keys and server: everything the owner does.</summary>
internal sealed class MainForm : Form
{
    private readonly LicenceBook _book;
    private readonly OwnerKeys _keys;
    private readonly DataGridView _licences = Grid();
    private readonly DataGridView _customers = Grid();
    private readonly Label _syncStatus = Label("");

    public MainForm(LicenceBook book, OwnerKeys keys)
    {
        _book = book;
        _keys = keys;
        Text = "Stone's Hotspot — LicenseManager";
        Width = 1100;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(LicencesTab());
        tabs.TabPages.Add(CustomersTab());
        tabs.TabPages.Add(KeysTab());
        tabs.TabPages.Add(ServerTab());
        Controls.Add(tabs);
        Reload(sync: true);
    }

    private TabPage LicencesTab()
    {
        var page = new TabPage(T("Licences", "Licences"));
        var buttons = Row(
            Button(T("Nouvelle licence", "New licence"), (_, _) => NewLicence()),
            Button(T("Renouveler", "Renew"), (_, _) => WithSelected(Renew)),
            Button(T("Révoquer", "Revoke"), (_, _) => WithSelected(Revoke)),
            Button(T("Partager le code", "Share the code"), (_, _) => WithSelected(l => ShareDialog.Show(this, l))),
            Button(T("Exporter CSV", "Export CSV"), (_, _) => ExportCsv()),
            Button(T("Synchroniser", "Sync"), (_, _) => Reload(sync: true)),
            _syncStatus);
        buttons.Dock = DockStyle.Top;
        _licences.Dock = DockStyle.Fill;
        page.Controls.Add(_licences);
        page.Controls.Add(buttons);
        return page;
    }

    private TabPage CustomersTab()
    {
        var page = new TabPage(T("Clients", "Customers"));
        var buttons = Row(Button(T("Nouveau client", "New customer"), (_, _) =>
        {
            if (CustomerDialog.Show(this, _book) is not null)
                Reload(sync: false);
        }));
        buttons.Dock = DockStyle.Top;
        _customers.Dock = DockStyle.Fill;
        page.Controls.Add(_customers);
        page.Controls.Add(buttons);
        return page;
    }

    private TabPage KeysTab()
    {
        var page = new TabPage(T("Clés et sauvegarde", "Keys and backup"));
        var column = Column();
        column.Controls.Add(Label(T("Clés publiques (à mettre dans le hotspot : src/StonesHotspot.Licensing/Keys)", "Public keys (go into the hotspot build: src/StonesHotspot.Licensing/Keys)"), bold: true));
        column.Controls.Add(Label("licence-public.txt"));
        column.Controls.Add(ReadOnly(Convert.ToBase64String(_keys.LicencePublic)));
        column.Controls.Add(Label("server-public.txt"));
        column.Controls.Add(ReadOnly(Convert.ToBase64String(_keys.ServerPublic)));
        column.Controls.Add(Label(T("Clé du serveur de licences (secrète : seulement dans appsettings.Local.json du serveur)", "Licence server key (secret: only in the server's appsettings.Local.json)"), bold: true));
        column.Controls.Add(Button(T("Copier la clé privée du serveur", "Copy the server private key"), (_, _) =>
        {
            if (Confirm(this, T("Cette clé permet de signer les réponses du serveur. Ne la collez que dans la configuration du serveur. Copier ?",
                    "This key signs the server's answers. Paste it only into the server configuration. Copy?")))
                Clipboard.SetText(Convert.ToBase64String(_keys.ServerPrivate));
        }));
        column.Controls.Add(Label(T("Sauvegarde", "Backup"), bold: true));
        column.Controls.Add(Button(T("Sauvegarder licences + clés (zip)", "Back up licences + keys (zip)"), (_, _) =>
        {
            using var dialog = new SaveFileDialog { FileName = $"licencemanager-{DateTime.Now:yyyyMMdd-HHmm}.zip", Filter = "Zip (*.zip)|*.zip" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            File.Delete(dialog.FileName);
            _book.Backup(dialog.FileName, ManagerFiles.KeyFile);
            Info(this, T("Sauvegarde enregistrée. Le fichier de clés y reste chiffré par votre mot de passe.", "Backup saved. The key file inside stays encrypted with your password."));
        }));
        column.Controls.Add(Button(T("Copier le fichier de clés…", "Copy the key file…"), (_, _) => FirstRun.ForceBackup(this)));
        page.Controls.Add(column);
        return page;
    }

    private TabPage ServerTab()
    {
        var page = new TabPage(T("Serveur", "Server"));
        var settings = ManagerFiles.LoadSettings();
        var column = Column();
        var url = Box(width: 480);
        url.Text = settings.ServerUrl;
        var token = Box(password: true, width: 480);
        token.Text = settings.Token;
        column.Controls.Add(Label(T("Adresse du serveur de licences (https://…)", "Licence server address (https://…)")));
        column.Controls.Add(url);
        column.Controls.Add(Label(T("Jeton (LicenceServer:ManagerToken du serveur)", "Token (the server's LicenceServer:ManagerToken)")));
        column.Controls.Add(token);
        column.Controls.Add(Button(T("Enregistrer et synchroniser", "Save and sync"), (_, _) =>
        {
            ManagerFiles.SaveSettings(new ManagerSettings(url.Text.Trim(), token.Text.Trim()));
            Reload(sync: true);
        }));
        column.Controls.Add(Label(T("Sans serveur, tout fonctionne : les licences seront envoyées à la prochaine synchronisation.",
            "Without a server everything works: licences are sent at the next sync.")));
        page.Controls.Add(column);
        return page;
    }

    private void NewLicence()
    {
        if (LicenceDialog.Show(this, _book) is { } licence)
        {
            Reload(sync: true);
            ShareDialog.Show(this, licence);
        }
    }

    private void Renew(IssuedLicence licence)
    {
        var days = DaysDialog.Show(this, T($"Renouveler la licence {licence.Id} (expire le {licence.ExpiresOn:dd/MM/yyyy})", $"Renew licence {licence.Id} (ends {licence.ExpiresOn:dd/MM/yyyy})"));
        if (days is null)
            return;
        try
        {
            var renewed = _book.Renew(licence.Id, days.Value);
            Reload(sync: true);
            ShareDialog.Show(this, renewed);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error(this, ex.Message);
        }
    }

    private void Revoke(IssuedLicence licence)
    {
        if (!Confirm(this, T($"Révoquer la licence {licence.Id} de {licence.CustomerName} ? Le hotspot s'arrêtera 7 jours après son prochain contact avec le serveur.",
                $"Revoke licence {licence.Id} of {licence.CustomerName}? The hotspot stops 7 days after its next contact with the server.")))
            return;
        _book.Revoke(licence.Id, T("Révoquée dans LicenseManager", "Revoked in LicenseManager"));
        Reload(sync: true);
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog { FileName = $"licences-{DateTime.Now:yyyyMMdd}.csv", Filter = "CSV (*.csv)|*.csv" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllBytes(dialog.FileName, _book.ExportCsv());
    }

    private void WithSelected(Action<IssuedLicence> action)
    {
        if (_licences.CurrentRow?.Tag is IssuedLicence licence)
            action(licence);
        else
            Info(this, T("Choisissez d'abord une licence dans la liste.", "Select a licence in the list first."));
    }

    private void Reload(bool sync)
    {
        _licences.Rows.Clear();
        if (_licences.Columns.Count == 0)
        {
            foreach (var header in new[] { "N°", T("Client", "Customer"), "Machine ID", T("Édition", "Edition"), T("Émise", "Issued"), T("Expire", "Expires"), T("État", "State"), T("Serveur", "Server") })
                _licences.Columns.Add(header, header);
        }
        foreach (var l in _book.Licences())
        {
            var state = l.Revoked ? T("Révoquée", "Revoked") : l.ExpiresOn < DateOnly.FromDateTime(DateTime.UtcNow) ? T("Expirée", "Expired") : T("Active", "Active");
            var row = _licences.Rows[_licences.Rows.Add(l.Id, l.CustomerName, l.MachineId, l.Edition, l.IssuedOn.ToString("dd/MM/yyyy"), l.ExpiresOn.ToString("dd/MM/yyyy"),
                state, l.Synced ? "✓" : T("à envoyer", "to send"))];
            row.Tag = l;
        }

        _customers.Rows.Clear();
        if (_customers.Columns.Count == 0)
        {
            foreach (var header in new[] { T("Nom", "Name"), T("Téléphone", "Phone"), T("Commerce", "Business"), T("Créé le", "Created") })
                _customers.Columns.Add(header, header);
        }
        foreach (var c in _book.Customers())
            _customers.Rows.Add(c.Name, c.Phone, c.Business, c.CreatedAt.LocalDateTime.ToString("dd/MM/yyyy"));

        if (sync)
            _ = SyncAsync();
    }

    private async Task SyncAsync()
    {
        var settings = ManagerFiles.LoadSettings();
        var pending = _book.Unsynced().Count;
        if (settings.ServerUrl.Length == 0 || !Uri.TryCreate(settings.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute, out var server))
        {
            _syncStatus.Text = T($"Pas de serveur configuré ({pending} à envoyer)", $"No server configured ({pending} to send)");
            return;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var sent = await new ManagerSync(http, server, settings.Token).PushAsync(_book);
            _syncStatus.Text = T($"Synchronisé ({sent} envoyée(s))", $"Synced ({sent} sent)");
            if (sent > 0)
                Reload(sync: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _syncStatus.Text = T($"Serveur injoignable, {pending} à envoyer plus tard", $"Server unreachable, {pending} to send later");
        }
    }

    private static DataGridView Grid() => new()
    {
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false,
    };

    private static TextBox ReadOnly(string text) => new() { Text = text, ReadOnly = true, Width = 520 };
}
