using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace P2pb2b
{
    public partial class TokenChangeForm : Form
    {
        public string Token { get; private set; }

        public TokenChangeForm()
        {
            InitializeComponent();
        }

        private void Submit_Click(object sender, EventArgs e)
        {
            Token = TokenInput.Text;
            this.Close();
        }
    }
}
