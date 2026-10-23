using StonesHotspot.LicenseIssuer;
using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Codes;
using static StonesHotspot.LicenseManager.Ui;

namespace StonesHotspot.LicenseManager;

internal static class CustomerDialog
{
    public static Customer? Show(IWin32Window owner, LicenceBook book)
    {
        var column = Column();
        var name = Box();
        var phone = Box();
        var business = Box();
        column.Controls.Add(Label(T("Nom du client", "Customer name")));
        column.Controls.Add(name);
        column.Controls.Add(Label(T("Téléphone (WhatsApp)", "Phone (WhatsApp)")));
        column.Controls.Add(phone);
        column.Controls.Add(Label(T("Commerce (imprimé dans la licence)", "Business (written in the licence)")));
        column.Controls.Add(business);
        Customer? created = null;
        using var form = Dialog(T("Nouveau client", "New customer"), column, 440);
        column.Controls.Add(Button(T("Enregistrer", "Save"), (_, _) =>
        {
            try
            {
                created = book.AddCustomer(name.Text, phone.Text, business.Text);
                form.DialogResult = DialogResult.OK;
            }
            catch (ArgumentException ex)
            {
                Error(form, ex.Message);
            }
        }));
        return form.ShowDialog(owner) == DialogResult.OK ? created : null;
    }
}

internal static class DaysDialog
{
    /// <summary>1 year, or a number of days.</summary>
    public static int? Show(IWin32Window owner, string title)
    {
        var column = Column();
        var year = new RadioButton { Text = T("1 an (365 jours)", "1 year (365 days)"), Checked = true, AutoSize = true };
        var custom = new RadioButton { Text = T("Nombre de jours :", "Number of days:"), AutoSize = true };
        var days = new NumericUpDown { Minimum = 1, Maximum = 3660, Value = 30, Width = 100 };
        column.Controls.Add(year);
        column.Controls.Add(Row(custom, days));
        int? result = null;
        using var form = Dialog(title, column, 420);
        column.Controls.Add(Button("OK", (_, _) =>
        {
            result = year.Checked ? 365 : (int)days.Value;
            form.DialogResult = DialogResult.OK;
        }));
        return form.ShowDialog(owner) == DialogResult.OK ? result : null;
    }
}

internal static class LicenceDialog
{
    public static IssuedLicence? Show(IWin32Window owner, LicenceBook book)
    {
        var column = Column();
        var customers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360, DisplayMember = nameof(Customer.Name) };
        void LoadCustomers(Customer? select = null)
        {
            customers.Items.Clear();
            foreach (var c in book.Customers())
                customers.Items.Add(c);
            customers.SelectedItem = select is null ? (customers.Items.Count > 0 ? customers.Items[0] : null)
                : customers.Items.Cast<Customer>().FirstOrDefault(c => c.Id == select.Id);
        }
        LoadCustomers();
        var machine = Box();
        var pro = new RadioButton { Text = "Pro", Checked = true, AutoSize = true };
        var basic = new RadioButton { Text = "Basic", AutoSize = true };
        var year = new RadioButton { Text = T("1 an", "1 year"), Checked = true, AutoSize = true };
        var custom = new RadioButton { Text = T("jours :", "days:"), AutoSize = true };
        var days = new NumericUpDown { Minimum = 1, Maximum = 3660, Value = 30, Width = 90 };

        column.Controls.Add(Label(T("Client", "Customer")));
        column.Controls.Add(Row(customers, Button(T("Nouveau…", "New…"), (_, _) =>
        {
            if (CustomerDialog.Show(owner, book) is { } c)
                LoadCustomers(c);
        })));
        column.Controls.Add(Label(T("Machine ID (reçu par WhatsApp, ex. MID-7K3Q-9XPA-2LMD)", "Machine ID (received by WhatsApp, e.g. MID-7K3Q-9XPA-2LMD)")));
        column.Controls.Add(machine);
        column.Controls.Add(Label(T("Édition", "Edition")));
        var editions = new FlowLayoutPanel { AutoSize = true };
        editions.Controls.AddRange([pro, basic]);
        column.Controls.Add(editions);
        column.Controls.Add(Label(T("Durée", "Duration")));
        var duration = new FlowLayoutPanel { AutoSize = true };
        duration.Controls.AddRange([year, custom, days]);
        column.Controls.Add(duration);

        IssuedLicence? issued = null;
        using var form = Dialog(T("Nouvelle licence", "New licence"), column);
        column.Controls.Add(Button(T("Créer la licence", "Create the licence"), (_, _) =>
        {
            if (customers.SelectedItem is not Customer customer)
            {
                Error(form, T("Choisissez ou créez un client.", "Choose or create a customer."));
                return;
            }
            if (!MachineId.TryParse(machine.Text, out _))
            {
                Error(form, T("Machine ID invalide : il a la forme MID-XXXX-XXXX-XXXX.", "Invalid Machine ID: it looks like MID-XXXX-XXXX-XXXX."));
                return;
            }
            issued = book.Issue(customer.Id, machine.Text, pro.Checked ? Edition.Pro : Edition.Basic, year.Checked ? 365 : (int)days.Value);
            form.DialogResult = DialogResult.OK;
        }));
        return form.ShowDialog(owner) == DialogResult.OK ? issued : null;
    }
}

/// <summary>The finished code: copy, WhatsApp, .lic file, QR image.</summary>
internal static class ShareDialog
{
    public static void Show(IWin32Window owner, IssuedLicence licence)
    {
        var column = Column();
        column.Controls.Add(Label(T($"Licence {licence.Id} — {licence.Edition} — {licence.CustomerName} — jusqu'au {licence.ExpiresOn:dd/MM/yyyy}",
            $"Licence {licence.Id} — {licence.Edition} — {licence.CustomerName} — until {licence.ExpiresOn:dd/MM/yyyy}"), bold: true));
        column.Controls.Add(new TextBox { Text = licence.Code, Multiline = true, ReadOnly = true, Width = 600, Height = 110, Font = new Font(FontFamily.GenericMonospace, 10) });
        using var form = Dialog(T("Code de licence", "Licence code"), column, 660);
        column.Controls.Add(Row(
            Button(T("Copier le message", "Copy the message"), (_, _) => Clipboard.SetText(Sharing.Message(licence))),
            Button("WhatsApp", (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Sharing.WhatsAppLink(licence)) { UseShellExecute = true })),
            Button(T("Enregistrer .lic", "Save .lic"), (_, _) => Save(form, $"licence-{licence.Id}.lic", "Licence (*.lic)|*.lic", System.Text.Encoding.UTF8.GetBytes(Sharing.LicFile(licence)))),
            Button(T("Enregistrer le QR", "Save the QR"), (_, _) => Save(form, $"licence-{licence.Id}.png", "PNG (*.png)|*.png", Sharing.QrPng(licence))),
            Button(T("Fermer", "Close"), (_, _) => form.Close())));
        form.ShowDialog(owner);
    }

    private static void Save(IWin32Window owner, string name, string filter, byte[] content)
    {
        using var dialog = new SaveFileDialog { FileName = name, Filter = filter };
        if (dialog.ShowDialog(owner) == DialogResult.OK)
            File.WriteAllBytes(dialog.FileName, content);
    }
}
