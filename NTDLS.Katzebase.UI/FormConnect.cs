﻿using NTDLS.Katzebase.Client;

namespace NTDLS.Katzebase.UI
{
    public partial class FormConnect : Form
    {
        public string ServerAddress => textBoxServerAddress.Text.Trim();
        public string ServerPort => textBoxPort.Text.Trim();
        public string ServerAddressURL => $"http://{ServerAddress}:{ServerPort}/";

        public FormConnect()
        {
            InitializeComponent();
        }

        private void FormConnect_Load(object sender, EventArgs e)
        {
            textBoxServerAddress.Text = "localhost";
            textBoxPort.Text = "6858";

            AcceptButton = buttonConnect;
            CancelButton = buttonCancel;
        }

        private void buttonOk_Click(object sender, EventArgs e)
        {
            try
            {
                using (var client = new KbClient(ServerAddressURL))
                {
                    if (client.Server.Ping().Success)
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to connect to the specified server: \"{ex.Message}\".", KbConstants.FriendlyName);
            }
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}