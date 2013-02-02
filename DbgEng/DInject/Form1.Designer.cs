namespace DHijack
{
	partial class Form1
	{
		/// <summary>
		/// Erforderliche Designervariable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Verwendete Ressourcen bereinigen.
		/// </summary>
		/// <param name="disposing">True, wenn verwaltete Ressourcen gelöscht werden sollen; andernfalls False.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Vom Windows Form-Designer generierter Code

		/// <summary>
		/// Erforderliche Methode für die Designerunterstützung.
		/// Der Inhalt der Methode darf nicht mit dem Code-Editor geändert werden.
		/// </summary>
		private void InitializeComponent()
		{
			this.label2 = new System.Windows.Forms.Label();
			this.pid = new System.Windows.Forms.TextBox();
			this.label3 = new System.Windows.Forms.Label();
			this.input_objAddress = new System.Windows.Forms.TextBox();
			this.button3 = new System.Windows.Forms.Button();
			this.objString = new System.Windows.Forms.TextBox();
			this.label4 = new System.Windows.Forms.Label();
			this.button1 = new System.Windows.Forms.Button();
			this.SuspendLayout();
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Location = new System.Drawing.Point(12, 9);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(145, 13);
			this.label2.TabIndex = 2;
			this.label2.Text = "Program id/name to attach to";
			// 
			// pid
			// 
			this.pid.Location = new System.Drawing.Point(15, 25);
			this.pid.Name = "pid";
			this.pid.Size = new System.Drawing.Size(121, 20);
			this.pid.TabIndex = 3;
			this.pid.Text = "myprogram";
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(12, 50);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(78, 13);
			this.label3.TabIndex = 6;
			this.label3.Text = "Object address";
			// 
			// input_objAddress
			// 
			this.input_objAddress.Location = new System.Drawing.Point(15, 66);
			this.input_objAddress.Name = "input_objAddress";
			this.input_objAddress.Size = new System.Drawing.Size(121, 20);
			this.input_objAddress.TabIndex = 7;
			// 
			// button3
			// 
			this.button3.Location = new System.Drawing.Point(160, 64);
			this.button3.Name = "button3";
			this.button3.Size = new System.Drawing.Size(156, 23);
			this.button3.TabIndex = 8;
			this.button3.Text = "Execute toString()";
			this.button3.UseVisualStyleBackColor = true;
			this.button3.Click += new System.EventHandler(this.ExecuteToString);
			// 
			// objString
			// 
			this.objString.Location = new System.Drawing.Point(15, 105);
			this.objString.Name = "objString";
			this.objString.ReadOnly = true;
			this.objString.Size = new System.Drawing.Size(301, 20);
			this.objString.TabIndex = 9;
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(12, 89);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(69, 13);
			this.label4.TabIndex = 10;
			this.label4.Text = "Object string:";
			// 
			// button1
			// 
			this.button1.Location = new System.Drawing.Point(160, 23);
			this.button1.Name = "button1";
			this.button1.Size = new System.Drawing.Size(156, 23);
			this.button1.TabIndex = 11;
			this.button1.Text = "Inject into process";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new System.EventHandler(this.InjectIntoProcess);
			// 
			// Form1
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(330, 140);
			this.Controls.Add(this.button1);
			this.Controls.Add(this.label4);
			this.Controls.Add(this.objString);
			this.Controls.Add(this.button3);
			this.Controls.Add(this.input_objAddress);
			this.Controls.Add(this.label3);
			this.Controls.Add(this.pid);
			this.Controls.Add(this.label2);
			this.Name = "Form1";
			this.Text = "Form1";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox pid;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.TextBox input_objAddress;
		private System.Windows.Forms.Button button3;
		private System.Windows.Forms.TextBox objString;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Button button1;
	}
}

