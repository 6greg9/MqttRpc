namespace TestApp;

public partial class Form2 : Form
{
    private readonly Form1 _form1;

    public Form2(Form1 form1)
    {
        InitializeComponent();
        _form1 = form1;
    }

    private async void button1_Click(object sender, EventArgs e)
    {
        try
        {
            await _form1.RebootAsync(textBox1.Text);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "MqttRpc call failed");
        }
    }

    private void button2_Click(object sender, EventArgs e)
    {
        _form1.button1_Click(sender, e);
    }
}
