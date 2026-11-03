namespace GameMonitor
{
    public class PasswordPromptForm : Form
    {
        private TextBox txtPassword;
        private Button btnOk;
        private Button btnCancel;

        public string Password => txtPassword.Text;

        public PasswordPromptForm(string title)
        {
            Text = title;
            Width = 300;
            Height = 160;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Icon = SystemIcons.Shield;

            Controls.Add(new Label { Text = "请输入管理密码：", Left = 20, Top = 20, Width = 200 });

            txtPassword = new TextBox { Left = 20, Top = 45, Width = 240, UseSystemPasswordChar = true };
            Controls.Add(txtPassword);

            btnOk = new Button { Text = "确定", Left = 95, Top = 80, Width = 80, DialogResult = DialogResult.OK };
            btnCancel = new Button { Text = "取消", Left = 185, Top = 80, Width = 80, DialogResult = DialogResult.Cancel };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }
}