namespace MMBLAI;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.NotifyIcon notifyIcon1;
    private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
    private System.Windows.Forms.ToolStripMenuItem showConsoleToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
    private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;

    private System.Windows.Forms.Label lblLlamaDir;
    private System.Windows.Forms.TextBox txtLlamaDir;
    private System.Windows.Forms.Button btnLlamaDir;

    private System.Windows.Forms.Label lblModelDir;
    private System.Windows.Forms.TextBox txtModelDir;
    private System.Windows.Forms.Button btnModelDir;

    private System.Windows.Forms.Button btnStop;
    private System.Windows.Forms.Button btnClearLog;

    private System.Windows.Forms.Label lblModels;
    private System.Windows.Forms.Label lblRunningModel;
    private System.Windows.Forms.ListBox lstModels;

    private System.Windows.Forms.Label lblPort;
    private System.Windows.Forms.TextBox txtPort;
    private System.Windows.Forms.Button btnSetPort;
    private System.Windows.Forms.Button btnCopyApi;

    private System.Windows.Forms.Label lblGpuLayers;
    private System.Windows.Forms.TextBox txtGpuLayers;
    private System.Windows.Forms.Label lblCtx;
    private System.Windows.Forms.TextBox txtCtx;
    private System.Windows.Forms.Label lblOtherArgs;
    private System.Windows.Forms.TextBox txtOtherArgs;
    private System.Windows.Forms.ToolTip toolTip1;

    private System.Windows.Forms.Label lblLog;
    private LogCheckBox chkLogInput;
    private LogCheckBox chkLogOutput;
    private System.Windows.Forms.RichTextBox txtLog;

    private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        notifyIcon1 = new System.Windows.Forms.NotifyIcon(components);
        contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(components);
        showConsoleToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
        exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();

        lblLlamaDir = new System.Windows.Forms.Label();
        txtLlamaDir = new System.Windows.Forms.TextBox();
        btnLlamaDir = new System.Windows.Forms.Button();

        lblModelDir = new System.Windows.Forms.Label();
        txtModelDir = new System.Windows.Forms.TextBox();
        btnModelDir = new System.Windows.Forms.Button();

        btnStop = new System.Windows.Forms.Button();
        btnClearLog = new System.Windows.Forms.Button();

        lblModels = new System.Windows.Forms.Label();
        lblRunningModel = new System.Windows.Forms.Label();
        lstModels = new System.Windows.Forms.ListBox();

        lblPort = new System.Windows.Forms.Label();
        txtPort = new System.Windows.Forms.TextBox();
        btnSetPort = new System.Windows.Forms.Button();
        btnCopyApi = new System.Windows.Forms.Button();

        lblGpuLayers = new System.Windows.Forms.Label();
        txtGpuLayers = new System.Windows.Forms.TextBox();
        lblCtx = new System.Windows.Forms.Label();
        txtCtx = new System.Windows.Forms.TextBox();
        lblOtherArgs = new System.Windows.Forms.Label();
        txtOtherArgs = new System.Windows.Forms.TextBox();
        toolTip1 = new System.Windows.Forms.ToolTip();

        lblLog = new System.Windows.Forms.Label();
        chkLogInput = new LogCheckBox();
        chkLogOutput = new LogCheckBox();
        txtLog = new System.Windows.Forms.RichTextBox();

        folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();

        contextMenuStrip1.SuspendLayout();
        SuspendLayout();

        // notifyIcon1
        notifyIcon1.ContextMenuStrip = contextMenuStrip1;
        notifyIcon1.Visible = true;
        notifyIcon1.MouseDoubleClick += notifyIcon1_MouseDoubleClick;

        // contextMenuStrip1
        contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { showConsoleToolStripMenuItem, toolStripSeparator1, exitToolStripMenuItem });
        contextMenuStrip1.Name = "contextMenuStrip1";
        contextMenuStrip1.Size = new System.Drawing.Size(143, 76);
        contextMenuStrip1.Opening += contextMenuStrip1_Opening;

        // showConsoleToolStripMenuItem
        showConsoleToolStripMenuItem.Name = "showConsoleToolStripMenuItem";
        showConsoleToolStripMenuItem.Size = new System.Drawing.Size(142, 22);
        showConsoleToolStripMenuItem.Text = "打开控制台";
        showConsoleToolStripMenuItem.Click += showConsoleToolStripMenuItem_Click;

        // toolStripSeparator1
        toolStripSeparator1.Name = "toolStripSeparator1";
        toolStripSeparator1.Size = new System.Drawing.Size(139, 6);

        // exitToolStripMenuItem
        exitToolStripMenuItem.Name = "exitToolStripMenuItem";
        exitToolStripMenuItem.Size = new System.Drawing.Size(142, 22);
        exitToolStripMenuItem.Text = "退出";
        exitToolStripMenuItem.Click += exitToolStripMenuItem_Click;

        // Row 1: y=12, height=28
        // lblLlamaDir
        lblLlamaDir.AutoSize = true;
        lblLlamaDir.Location = new System.Drawing.Point(12, 18);
        lblLlamaDir.Name = "lblLlamaDir";
        lblLlamaDir.Size = new System.Drawing.Size(68, 15);
        lblLlamaDir.TabIndex = 0;
        lblLlamaDir.Text = "llama.cpp:";
        lblLlamaDir.ForeColor = Color.FromArgb(169, 183, 198);

        // txtLlamaDir - fixed height via Multiline
        txtLlamaDir.BackColor = Color.FromArgb(60, 63, 65);
        txtLlamaDir.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        txtLlamaDir.ForeColor = Color.FromArgb(169, 183, 198);
        txtLlamaDir.Location = new System.Drawing.Point(86, 12);
        txtLlamaDir.Multiline = true;
        txtLlamaDir.Name = "txtLlamaDir";
        txtLlamaDir.ReadOnly = true;
        txtLlamaDir.ShortcutsEnabled = false;
        txtLlamaDir.TabStop = false;
        txtLlamaDir.Cursor = System.Windows.Forms.Cursors.Hand;
        txtLlamaDir.Size = new System.Drawing.Size(854, 28);
        txtLlamaDir.TabIndex = 1;
        txtLlamaDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtLlamaDir.Click += txtLlamaDir_Click;
        toolTip1.SetToolTip(txtLlamaDir, "点击在资源管理器中打开该目录");

        // btnLlamaDir
        btnLlamaDir.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnLlamaDir.FlatAppearance.BorderColor = Color.FromArgb(78, 82, 84);
        btnLlamaDir.FlatAppearance.MouseOverBackColor = Color.FromArgb(75, 75, 75);
        btnLlamaDir.BackColor = Color.FromArgb(60, 63, 65);
        btnLlamaDir.ForeColor = Color.FromArgb(255, 165, 0);
        btnLlamaDir.Font = new Font("Segoe UI Emoji", 12F);
        btnLlamaDir.Location = new System.Drawing.Point(946, 12);
        btnLlamaDir.Name = "btnLlamaDir";
        btnLlamaDir.Size = new System.Drawing.Size(32, 28);
        btnLlamaDir.TabIndex = 2;
        btnLlamaDir.Text = "📂";
        btnLlamaDir.UseVisualStyleBackColor = false;
        btnLlamaDir.Click += btnLlamaDir_Click;
        btnLlamaDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        // Row 2: y=42, height=28
        // lblModelDir
        lblModelDir.AutoSize = true;
        lblModelDir.Location = new System.Drawing.Point(12, 48);
        lblModelDir.Name = "lblModelDir";
        lblModelDir.Size = new System.Drawing.Size(56, 15);
        lblModelDir.TabIndex = 3;
        lblModelDir.Text = "模型目录:";
        lblModelDir.ForeColor = Color.FromArgb(169, 183, 198);

        // txtModelDir - fixed height via Multiline
        txtModelDir.BackColor = Color.FromArgb(60, 63, 65);
        txtModelDir.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        txtModelDir.ForeColor = Color.FromArgb(169, 183, 198);
        txtModelDir.Location = new System.Drawing.Point(86, 42);
        txtModelDir.Multiline = true;
        txtModelDir.Name = "txtModelDir";
        txtModelDir.ReadOnly = true;
        txtModelDir.ShortcutsEnabled = false;
        txtModelDir.TabStop = false;
        txtModelDir.Cursor = System.Windows.Forms.Cursors.Hand;
        txtModelDir.Size = new System.Drawing.Size(854, 28);
        txtModelDir.TabIndex = 4;
        txtModelDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtModelDir.Click += txtModelDir_Click;
        toolTip1.SetToolTip(txtModelDir, "点击在资源管理器中打开该目录");

        // btnModelDir
        btnModelDir.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnModelDir.FlatAppearance.BorderColor = Color.FromArgb(78, 82, 84);
        btnModelDir.FlatAppearance.MouseOverBackColor = Color.FromArgb(75, 75, 75);
        btnModelDir.BackColor = Color.FromArgb(60, 63, 65);
        btnModelDir.ForeColor = Color.FromArgb(255, 165, 0);
        btnModelDir.Font = new Font("Segoe UI Emoji", 12F);
        btnModelDir.Location = new System.Drawing.Point(946, 42);
        btnModelDir.Name = "btnModelDir";
        btnModelDir.Size = new System.Drawing.Size(32, 28);
        btnModelDir.TabIndex = 5;
        btnModelDir.Text = "📂";
        btnModelDir.UseVisualStyleBackColor = false;
        btnModelDir.Click += btnModelDir_Click;
        btnModelDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        // Row 3: y=72 (API端口设置)
        // lblPort
        lblPort.AutoSize = true;
        lblPort.Location = new System.Drawing.Point(12, 78);
        lblPort.Name = "lblPort";
        lblPort.Size = new System.Drawing.Size(56, 15);
        lblPort.TabIndex = 6;
        lblPort.Text = "API端口:";
        lblPort.ForeColor = Color.FromArgb(169, 183, 198);

        // txtPort - height 28 to match buttons (use Multiline to fix height)
        txtPort.BackColor = Color.FromArgb(60, 63, 65);
        txtPort.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        txtPort.ForeColor = Color.FromArgb(169, 183, 198);
        txtPort.Font = new Font("Consolas", 9F);
        txtPort.Location = new System.Drawing.Point(86, 72);
        txtPort.Multiline = true;
        txtPort.Name = "txtPort";
        txtPort.Size = new System.Drawing.Size(80, 28);
        txtPort.TabIndex = 7;
        txtPort.Text = "7777";
        txtPort.TextAlign = HorizontalAlignment.Center;
        txtPort.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        txtPort.KeyPress += txtPort_KeyPress;

        // btnSetPort - height 28 to match other buttons
        btnSetPort.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnSetPort.FlatAppearance.BorderColor = Color.FromArgb(78, 82, 84);
        btnSetPort.FlatAppearance.MouseOverBackColor = Color.FromArgb(75, 75, 75);
        btnSetPort.BackColor = Color.FromArgb(60, 63, 65);
        btnSetPort.ForeColor = Color.FromArgb(169, 183, 198);
        btnSetPort.Font = new Font("Microsoft YaHei UI", 9F);
        btnSetPort.Location = new System.Drawing.Point(170, 72);
        btnSetPort.Name = "btnSetPort";
        btnSetPort.Size = new System.Drawing.Size(50, 28);
        btnSetPort.TabIndex = 8;
        btnSetPort.Text = "设置";
        btnSetPort.UseVisualStyleBackColor = false;
        btnSetPort.Click += btnSetPort_Click;

        // btnCopyApi - copy API URL button
        btnCopyApi.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnCopyApi.FlatAppearance.BorderColor = Color.FromArgb(78, 82, 84);
        btnCopyApi.FlatAppearance.MouseOverBackColor = Color.FromArgb(75, 75, 75);
        btnCopyApi.BackColor = Color.FromArgb(60, 63, 65);
        btnCopyApi.ForeColor = Color.FromArgb(169, 183, 198);
        btnCopyApi.Font = new Font("Microsoft YaHei UI", 9F);
        btnCopyApi.Location = new System.Drawing.Point(225, 72);
        btnCopyApi.Name = "btnCopyApi";
        btnCopyApi.Size = new System.Drawing.Size(70, 28);
        btnCopyApi.TabIndex = 17;
        btnCopyApi.Text = "复制API";
        btnCopyApi.UseVisualStyleBackColor = false;
        btnCopyApi.Click += btnCopyApi_Click;

        // lblGpuLayers
        lblGpuLayers.AutoSize = true;
        lblGpuLayers.Location = new System.Drawing.Point(301, 78);
        lblGpuLayers.Name = "lblGpuLayers";
        lblGpuLayers.Size = new System.Drawing.Size(56, 15);
        lblGpuLayers.TabIndex = 18;
        lblGpuLayers.Text = "GPU层数:";
        lblGpuLayers.ForeColor = Color.FromArgb(169, 183, 198);
        toolTip1.SetToolTip(lblGpuLayers, "自动检测：根据 GPU 显存和模型大小自动计算合适层数\r\n-1 → 将所有层都放到 GPU 运行\r\n0  → 纯 CPU 运行模型，不使用 GPU 加速\r\n正整数（如 20、35）→ 前 N 层放到 GPU，其余在 CPU；显存不够启动失败时手动填写即可");

        // txtGpuLayers - height 28 to match buttons
        txtGpuLayers.BackColor = Color.FromArgb(60, 63, 65);
        txtGpuLayers.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        txtGpuLayers.ForeColor = Color.FromArgb(169, 183, 198);
        txtGpuLayers.Font = new Font("Consolas", 9F);
        txtGpuLayers.Location = new System.Drawing.Point(361, 72);
        txtGpuLayers.Multiline = true;
        txtGpuLayers.Name = "txtGpuLayers";
        txtGpuLayers.Size = new System.Drawing.Size(80, 28);
        txtGpuLayers.TabIndex = 19;
        txtGpuLayers.Text = "";
        txtGpuLayers.TextAlign = HorizontalAlignment.Center;
        txtGpuLayers.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        txtGpuLayers.KeyPress += txtGpuLayers_KeyPress;

        // lblCtx
        lblCtx.AutoSize = true;
        lblCtx.Location = new System.Drawing.Point(447, 78);
        lblCtx.Name = "lblCtx";
        lblCtx.Size = new System.Drawing.Size(52, 15);
        lblCtx.TabIndex = 20;
        lblCtx.Text = "CTX值:";
        lblCtx.ForeColor = Color.FromArgb(169, 183, 198);
        toolTip1.SetToolTip(lblCtx, "手动指定上下文窗口大小（正整数）\r\n常用值：16384 / 32768 / 65536 / 131072 / 262144\r\n优先级：/run 接口参数 > 手动填写 > 程序自动计算\r\n留空则使用程序自动计算值");

        // txtCtx - height 28 to match buttons
        txtCtx.BackColor = Color.FromArgb(60, 63, 65);
        txtCtx.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        txtCtx.ForeColor = Color.FromArgb(169, 183, 198);
        txtCtx.Font = new Font("Consolas", 9F);
        txtCtx.Location = new System.Drawing.Point(499, 72);
        txtCtx.Multiline = true;
        txtCtx.Name = "txtCtx";
        txtCtx.Size = new System.Drawing.Size(80, 28);
        txtCtx.TabIndex = 21;
        txtCtx.Text = "";
        txtCtx.TextAlign = HorizontalAlignment.Center;
        txtCtx.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        txtCtx.KeyPress += txtCtx_KeyPress;

        // lblOtherArgs
        lblOtherArgs.AutoSize = true;
        lblOtherArgs.Location = new System.Drawing.Point(585, 78);
        lblOtherArgs.Name = "lblOtherArgs";
        lblOtherArgs.Size = new System.Drawing.Size(68, 15);
        lblOtherArgs.TabIndex = 22;
        lblOtherArgs.Text = "其他参数:";
        lblOtherArgs.ForeColor = Color.FromArgb(169, 183, 198);
        toolTip1.SetToolTip(lblOtherArgs, "自定义额外启动参数，会追加到 llama-server 命令末尾。\r\n例如：--jinja -ngl 99\r\n程序会自动合并并避免与内置参数重复。");

        // txtOtherArgs - long input for extra llama-server arguments
        txtOtherArgs.BackColor = Color.FromArgb(60, 63, 65);
        txtOtherArgs.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        txtOtherArgs.ForeColor = Color.FromArgb(169, 183, 198);
        txtOtherArgs.Font = new Font("Consolas", 9F);
        txtOtherArgs.Location = new System.Drawing.Point(653, 72);
        txtOtherArgs.Multiline = true;
        txtOtherArgs.Name = "txtOtherArgs";
        txtOtherArgs.AcceptsReturn = true;
        txtOtherArgs.WordWrap = true;
        txtOtherArgs.ScrollBars = System.Windows.Forms.ScrollBars.None;
        txtOtherArgs.Size = new System.Drawing.Size(287, 28);
        txtOtherArgs.TabIndex = 23;
        txtOtherArgs.Text = "";
        txtOtherArgs.TextAlign = HorizontalAlignment.Left;
        txtOtherArgs.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        txtOtherArgs.TextChanged += txtOtherArgs_TextChanged;

        // Row 4: y=108 (终止按钮 + 模型列表标签)
        // lblModels - bottom align with btnStop bottom (btnStop y=108, height=28, bottom=136)
        lblModels.AutoSize = true;
        lblModels.Location = new System.Drawing.Point(12, 121);
        lblModels.Name = "lblModels";
        lblModels.Size = new System.Drawing.Size(44, 15);
        lblModels.TabIndex = 10;
        lblModels.Text = "模型列表";
        lblModels.ForeColor = Color.FromArgb(169, 183, 198);
        lblModels.Cursor = Cursors.Hand;
        lblModels.Click += LblModels_Click;
        toolTip1.SetToolTip(lblModels, "点击刷新并切换排序：模型大小 / 模型名称");

        // btnStop (same row as lblModels)
        btnStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnStop.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        btnStop.FlatAppearance.MouseOverBackColor = Color.FromArgb(80, 80, 80);
        btnStop.BackColor = Color.FromArgb(80, 80, 80);
        btnStop.ForeColor = Color.FromArgb(120, 120, 120);
        btnStop.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        btnStop.Location = new System.Drawing.Point(903, 108);
        btnStop.Name = "btnStop";
        btnStop.Size = new System.Drawing.Size(75, 28);
        btnStop.TabIndex = 9;
        btnStop.Text = "终止";
        btnStop.UseVisualStyleBackColor = false;
        btnStop.Click += btnStop_Click;
        btnStop.MouseEnter += btnStop_MouseEnter;
        btnStop.MouseLeave += btnStop_MouseLeave;
        btnStop.Enabled = false;
        btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        // lstModels: y=140, height=218 (fixed height)
        lstModels.BackColor = Color.FromArgb(38, 38, 40);
        lstModels.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        lstModels.Font = new Font("Consolas", 9F);
        lstModels.ForeColor = Color.FromArgb(169, 183, 198);
        lstModels.FormattingEnabled = true;
        lstModels.HorizontalScrollbar = false;
        lstModels.ItemHeight = 32;
        lstModels.Location = new System.Drawing.Point(12, 140);
        lstModels.Name = "lstModels";
        lstModels.Size = new System.Drawing.Size(966, 218);
        lstModels.TabIndex = 8;
        lstModels.SelectedIndexChanged += lstModels_SelectedIndexChanged;
        lstModels.DoubleClick += lstModels_DoubleClick;
        lstModels.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // lblRunningModel: centered below model list, compact height
        lblRunningModel.AutoSize = false;
        lblRunningModel.Location = new System.Drawing.Point(12, 360);
        lblRunningModel.Name = "lblRunningModel";
        lblRunningModel.Size = new System.Drawing.Size(966, 16);
        lblRunningModel.TabIndex = 11;
        lblRunningModel.Text = "";
        lblRunningModel.ForeColor = Color.FromArgb(255, 165, 0);
        lblRunningModel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        lblRunningModel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblRunningModel.Cursor = Cursors.Hand;
        lblRunningModel.Click += lblRunningModel_Click;

        // Row: Log (y=342)
        // lblLog - bottom align with btnClearLog bottom (btnClearLog y=342, height=28, bottom=370)
        lblLog.AutoSize = true;
        lblLog.Location = new System.Drawing.Point(12, 387);
        lblLog.Name = "lblLog";
        lblLog.Size = new System.Drawing.Size(32, 15);
        lblLog.TabIndex = 12;
        lblLog.Text = "日志";
        lblLog.ForeColor = Color.FromArgb(169, 183, 198);

        // chkLogInput
        chkLogInput.AutoSize = false;
        chkLogInput.Location = new System.Drawing.Point(48, 385);
        chkLogInput.Name = "chkLogInput";
        chkLogInput.Size = new System.Drawing.Size(52, 19);
        chkLogInput.TabIndex = 24;
        chkLogInput.Text = "输入";
        chkLogInput.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        chkLogInput.ForeColor = Color.FromArgb(169, 183, 198);
        chkLogInput.CheckedChanged += chkLogInput_CheckedChanged;
        toolTip1.SetToolTip(chkLogInput, "勾选后记录 /v1/chat/completions 的输入；日志仅显示 [显示输入]，点击可查看 JSON。");

        // chkLogOutput
        chkLogOutput.AutoSize = false;
        chkLogOutput.Location = new System.Drawing.Point(90, 385);
        chkLogOutput.Name = "chkLogOutput";
        chkLogOutput.Size = new System.Drawing.Size(52, 19);
        chkLogOutput.TabIndex = 25;
        chkLogOutput.Text = "输出";
        chkLogOutput.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        chkLogOutput.ForeColor = Color.FromArgb(169, 183, 198);
        chkLogOutput.CheckedChanged += chkLogOutput_CheckedChanged;
        toolTip1.SetToolTip(chkLogOutput, "勾选后打印 /v1/chat/completions 的原始输出；点击 [输出] 可查看 JSON。");

        // btnClearLog (same row as lblLog)
        btnClearLog.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        btnClearLog.FlatAppearance.BorderColor = Color.FromArgb(78, 82, 84);
        btnClearLog.FlatAppearance.MouseOverBackColor = Color.FromArgb(78, 82, 84);
        btnClearLog.BackColor = Color.FromArgb(60, 63, 65);
        btnClearLog.ForeColor = Color.FromArgb(169, 183, 198);
        btnClearLog.Font = new Font("Microsoft YaHei UI", 9F);
        btnClearLog.Location = new System.Drawing.Point(903, 374);
        btnClearLog.Name = "btnClearLog";
        btnClearLog.Size = new System.Drawing.Size(75, 28);
        btnClearLog.TabIndex = 13;
        btnClearLog.Text = "清理";
        btnClearLog.UseVisualStyleBackColor = false;
        btnClearLog.Click += btnClearLog_Click;
        btnClearLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        // txtLog: y=374
        txtLog.BackColor = Color.FromArgb(30, 30, 30);
        txtLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
        txtLog.Font = new Font("Microsoft YaHei UI", 8F);
        txtLog.ForeColor = Color.FromArgb(169, 183, 198);
        txtLog.Location = new System.Drawing.Point(12, 406);
        txtLog.Name = "txtLog";
        txtLog.ReadOnly = true;
        txtLog.Size = new System.Drawing.Size(966, 264);
        txtLog.TabIndex = 14;
        txtLog.Text = "";
        txtLog.WordWrap = false;
        txtLog.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // Form1
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        BackColor = Color.FromArgb(43, 43, 43);
        ClientSize = new Size(990, 680);
        MinimumSize = new Size(520, 680);
        Controls.Add(txtLog);
        Controls.Add(chkLogOutput);
        Controls.Add(chkLogInput);
        Controls.Add(btnClearLog);
        Controls.Add(lblLog);
        Controls.Add(txtGpuLayers);
        Controls.Add(lblGpuLayers);
        Controls.Add(txtCtx);
        Controls.Add(lblCtx);
        Controls.Add(txtOtherArgs);
        Controls.Add(lblOtherArgs);
        Controls.Add(btnCopyApi);
        Controls.Add(btnSetPort);
        Controls.Add(txtPort);
        Controls.Add(lblPort);
        Controls.Add(lstModels);
        Controls.Add(lblRunningModel);
        Controls.Add(lblModels);
        Controls.Add(btnStop);
        Controls.Add(btnModelDir);
        Controls.Add(txtModelDir);
        Controls.Add(lblModelDir);
        Controls.Add(btnLlamaDir);
        Controls.Add(txtLlamaDir);
        Controls.Add(lblLlamaDir);
        DoubleBuffered = true;
        Font = new Font("Microsoft YaHei UI", 9F);
        ForeColor = Color.FromArgb(169, 183, 198);
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        Name = "Form1";
        StartPosition = FormStartPosition.Manual;
        Text = "MMBL-AI控制台";
        Load += Form1_Load;
        contextMenuStrip1.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
