using MqttRpc;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestApp
{
    public partial class Form2 : Form
    {
        Form1 _frm1;
        public Form2(Form1 frm1)
        {
            InitializeComponent();
            _frm1 = frm1;
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            var client = new MqttRpcClient("ws://localhost", 6666);
            await client.SimplePub("simple", textBox1.Text);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            _frm1.button1_Click(sender, e);
        }
    }
}
