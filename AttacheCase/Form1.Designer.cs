using System.Windows.Forms;
using AttacheCase.CustomControl;

namespace AttacheCase
{
  partial class Form1
  {
    /// <summary>
    /// 必要なデザイナー変数です。
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// 使用中のリソースをすべてクリーンアップします。
    /// </summary>
    /// <param name="disposing">マネージ リソースが破棄される場合 true、破棄されない場合は false です。</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "cts")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "_busy")]
    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        if (components != null)
          components.Dispose();
        if (_taskbarProgress != null)
        {
          _taskbarProgress.Dispose();
          _taskbarProgress = null;
        }
      }
      base.Dispose(disposing);
    }
    #region Windows フォーム デザイナーで生成されたコード

    /// <summary>
    /// デザイナー サポートに必要なメソッドです。このメソッドの内容を
    /// コード エディターで変更しないでください。
    /// </summary>
    private void InitializeComponent()
    {
      this.components = new System.ComponentModel.Container();
      System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
      this.statusStrip1 = new System.Windows.Forms.StatusStrip();
      this.toolStripStatusLabelDataVersion = new System.Windows.Forms.ToolStripStatusLabel();
      this.toolStripStatusLabelEncryptionTime = new System.Windows.Forms.ToolStripStatusLabel();
      this.toolStripStatusLabelLicense = new System.Windows.Forms.ToolStripStatusLabel();
      this.menuStrip1 = new System.Windows.Forms.MenuStrip();
      this.ToolStripMenuItemFile = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemEncrypt = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemEncryptSelectFiles = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemEncryptSelectFolder = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemDecrypt = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripMenuItem2 = new System.Windows.Forms.ToolStripSeparator();
      this.ToolStripMenuItemExit = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemOption = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemSetting = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemHelp = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemHelpContents = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripSeparator();
      this.ToolStripMenuItemAbout = new System.Windows.Forms.ToolStripMenuItem();
      this.panelOuter = new System.Windows.Forms.Panel();
      this.tabControl1 = new System.Windows.Forms.TabControl();
      this.tabPageStartPage = new System.Windows.Forms.TabPage();
      this.panelStartPage = new System.Windows.Forms.Panel();
      this.pictureBoxOption = new System.Windows.Forms.PictureBox();
      this.buttonExit = new System.Windows.Forms.Button();
      this.panelSidebarStart = new System.Windows.Forms.Panel();
      this.labelPostPpap = new System.Windows.Forms.Label();
      this.pictureBoxPostPpap = new System.Windows.Forms.PictureBox();
      this.labelDec = new System.Windows.Forms.Label();
      this.pictureBoxDec = new System.Windows.Forms.PictureBox();
      this.labelRsa = new System.Windows.Forms.Label();
      this.pictureBoxRsa = new System.Windows.Forms.PictureBox();
      this.labelAtc = new System.Windows.Forms.Label();
      this.pictureBoxAtc = new System.Windows.Forms.PictureBox();
      this.labelDragAndDrop = new System.Windows.Forms.Label();
      this.tabPageEncrypt = new System.Windows.Forms.TabPage();
      this.panelEncrypt = new System.Windows.Forms.Panel();
      this.labelPasswordStrength = new System.Windows.Forms.Label();
      this.checkBoxDeleteOriginalFileAfterEncryption = new System.Windows.Forms.CheckBox();
      this.pictureBoxPassStrengthMeter = new System.Windows.Forms.PictureBox();
      this.pictureBoxPasswordStrengthEmpty = new System.Windows.Forms.PictureBox();
      this.pictureBoxPasswordStrength04 = new System.Windows.Forms.PictureBox();
      this.pictureBoxPasswordStrength03 = new System.Windows.Forms.PictureBox();
      this.pictureBoxPasswordStrength02 = new System.Windows.Forms.PictureBox();
      this.pictureBoxPasswordStrength01 = new System.Windows.Forms.PictureBox();
      this.pictureBoxPasswordStrength00 = new System.Windows.Forms.PictureBox();
      this.pictureBoxEncryptBackToMain = new System.Windows.Forms.PictureBox();
      this.buttonEncryptionPasswordOk = new System.Windows.Forms.Button();
      this.panelSidebarEncrypt = new System.Windows.Forms.Panel();
      this.labelEncryption = new System.Windows.Forms.Label();
      this.pictureBoxEncryption = new System.Windows.Forms.PictureBox();
      this.buttonEncryptCancel = new System.Windows.Forms.Button();
      this.labelPassword = new System.Windows.Forms.Label();
      this.textBoxPassword = new AttacheCase.CustomControl.EyeDelayPasswordTextBox();
      this.tabPageEncryptConfirm = new System.Windows.Forms.TabPage();
      this.panelEncryptConfirm = new System.Windows.Forms.Panel();
      this.labelPasswordStrengthConfirm = new System.Windows.Forms.Label();
      this.pictureBoxPassStrengthMeterConfirm = new System.Windows.Forms.PictureBox();
      this.textBoxRePassword = new AttacheCase.CustomControl.EyeDelayPasswordTextBox();
      this.pictureBoxEncryptConfirmBackButton = new System.Windows.Forms.PictureBox();
      this.pictureBoxCheckPasswordValidation = new System.Windows.Forms.PictureBox();
      this.panelSidebarEncryptConfirm = new System.Windows.Forms.Panel();
      this.labelEncryptionConfirm = new System.Windows.Forms.Label();
      this.pictureBoxEncryptionConfirm = new System.Windows.Forms.PictureBox();
      this.buttonEncryptionConfirmCancel = new System.Windows.Forms.Button();
      this.buttonEncryptStart = new System.Windows.Forms.Button();
      this.pictureBoxInValidIcon = new System.Windows.Forms.PictureBox();
      this.pictureBoxValidIcon = new System.Windows.Forms.PictureBox();
      this.checkBoxReDeleteOriginalFileAfterEncryption = new System.Windows.Forms.CheckBox();
      this.labelInputPasswordAgain = new System.Windows.Forms.Label();
      this.tabPageDecrypt = new System.Windows.Forms.TabPage();
      this.panelDecrypt = new System.Windows.Forms.Panel();
      this.roundedBorderLabelSalvageMode = new AttacheCase.RoundedBorderLabel();
      this.textBoxDecryptPassword = new AttacheCase.CustomControl.EyeDelayPasswordTextBox();
      this.pictureBoxDecryptBackButton = new System.Windows.Forms.PictureBox();
      this.panelSidebarDecrypt = new System.Windows.Forms.Panel();
      this.labelDecryption = new System.Windows.Forms.Label();
      this.pictureBoxDecryption = new System.Windows.Forms.PictureBox();
      this.checkBoxDeleteAtcFileAfterDecryption = new System.Windows.Forms.CheckBox();
      this.buttonDecryptCancel = new System.Windows.Forms.Button();
      this.buttonDecryptStart = new System.Windows.Forms.Button();
      this.labelDecryptionPassword = new System.Windows.Forms.Label();
      this.tabPageRsa = new System.Windows.Forms.TabPage();
      this.panelRsa = new System.Windows.Forms.Panel();
      this.pictureBoxRsaBackButton = new System.Windows.Forms.PictureBox();
      this.labelRsaMessage = new System.Windows.Forms.Label();
      this.buttonRsaCancel = new System.Windows.Forms.Button();
      this.buttonGenerateKey = new System.Windows.Forms.Button();
      this.panelHeaderRsa = new System.Windows.Forms.Panel();
      this.labelRsaDetail = new System.Windows.Forms.Label();
      this.pictureBoxRsaPage = new System.Windows.Forms.PictureBox();
      this.tabPageRsaKey = new System.Windows.Forms.TabPage();
      this.panelRsaKey = new System.Windows.Forms.Panel();
      this.labelRsaInformation = new System.Windows.Forms.Label();
      this.pictureBoxInfoBalloon = new System.Windows.Forms.PictureBox();
      this.textBoxHashString = new System.Windows.Forms.TextBox();
      this.comboBoxHashList = new System.Windows.Forms.ComboBox();
      this.labelRsaDescription = new System.Windows.Forms.Label();
      this.pictureBoxPublicAndPrivateKey = new System.Windows.Forms.PictureBox();
      this.pictureBoxPrivateKey = new System.Windows.Forms.PictureBox();
      this.pictureBoxPublicKey = new System.Windows.Forms.PictureBox();
      this.pictureBoxRsaKeyBackButton = new System.Windows.Forms.PictureBox();
      this.buttonRsaKeyCancel = new System.Windows.Forms.Button();
      this.panelHeaderRsaKey = new System.Windows.Forms.Panel();
      this.labelRsaKeyName = new System.Windows.Forms.Label();
      this.pictureBoxRsaType = new System.Windows.Forms.PictureBox();
      this.tabPagePostPpapRegister = new System.Windows.Forms.TabPage();
      this.panelPostPpapRegister = new System.Windows.Forms.Panel();
      this.pictureBoxPostPpapRegisterBackButton = new System.Windows.Forms.PictureBox();
      this.progressBarPostPpap = new System.Windows.Forms.ProgressBar();
      this.labelPostPpapStatus = new System.Windows.Forms.Label();
      this.buttonRegisterYourMailAddress = new System.Windows.Forms.Button();
      this.labelYourMailAddress = new System.Windows.Forms.Label();
      this.textBoxYourMailAddress = new System.Windows.Forms.TextBox();
      this.labelPpapDescription = new System.Windows.Forms.Label();
      this.panelHeaderPostPpapRegister = new System.Windows.Forms.Panel();
      this.labelPostPpapPageTitle = new System.Windows.Forms.Label();
      this.pictureBoxPostPpapPage = new System.Windows.Forms.PictureBox();
      this.tabPagePostPpapMain = new System.Windows.Forms.TabPage();
      this.panelPostPpapMain = new System.Windows.Forms.Panel();
      this.pictureBoxMailAddress = new System.Windows.Forms.PictureBox();
      this.labelRegisteredMailAddress = new System.Windows.Forms.Label();
      this.pictureBoxInvalid = new System.Windows.Forms.PictureBox();
      this.pictureBoxKey = new System.Windows.Forms.PictureBox();
      this.pictureBoxLockIcon = new System.Windows.Forms.PictureBox();
      this.pictureBoxCheckIcon = new System.Windows.Forms.PictureBox();
      this.labelValidDetails = new System.Windows.Forms.Label();
      this.pictureBoxValidDetails = new System.Windows.Forms.PictureBox();
      this.labelValidMessage = new System.Windows.Forms.Label();
      this.pictureBoxValid = new System.Windows.Forms.PictureBox();
      this.buttonPuclicMailAddressSearch = new System.Windows.Forms.Button();
      this.labelRecipientMailAddress = new System.Windows.Forms.Label();
      this.textBoxPuclicRecipientMailAddress = new System.Windows.Forms.TextBox();
      this.pictureBoxPostPpapBackButton = new System.Windows.Forms.PictureBox();
      this.panelHeaderPostPpapMain = new System.Windows.Forms.Panel();
      this.labelPostPpapMain = new System.Windows.Forms.Label();
      this.pictureBoxPostPpapMain = new System.Windows.Forms.PictureBox();
      this.panelPublicDragAndDrop = new System.Windows.Forms.Panel();
      this.labelPasswordSharingIfRegistered = new System.Windows.Forms.Label();
      this.tabPageProgressState = new System.Windows.Forms.TabPage();
      this.panelProgressState = new System.Windows.Forms.Panel();
      this.pictureBoxProgressStateBackButton = new System.Windows.Forms.PictureBox();
      this.panelSidebarProgress = new System.Windows.Forms.Panel();
      this.labelProgress = new System.Windows.Forms.Label();
      this.pictureBoxProgress = new System.Windows.Forms.PictureBox();
      this.labelCryptionType = new System.Windows.Forms.Label();
      this.buttonCancel = new System.Windows.Forms.Button();
      this.labelProgressPercentText = new System.Windows.Forms.Label();
      this.labelProgressMessageText = new System.Windows.Forms.Label();
      this.progressBar = new System.Windows.Forms.ProgressBar();
      this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
      this.encryptToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.selectFilesToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
      this.selectFoldersToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
      this.decryptToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripMenuItem3 = new System.Windows.Forms.ToolStripSeparator();
      this.optionToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripMenuItem4 = new System.Windows.Forms.ToolStripSeparator();
      this.helpToolStripMenuItem2 = new System.Windows.Forms.ToolStripMenuItem();
      this.toolStripMenuItem5 = new System.Windows.Forms.ToolStripSeparator();
      this.exitToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
      this.contextMenuStrip2 = new System.Windows.Forms.ContextMenuStrip(this.components);
      this.ToolStripMenuItemAtcFile = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemExeFile = new System.Windows.Forms.ToolStripMenuItem();
      this.ToolStripMenuItemRsa = new System.Windows.Forms.ToolStripMenuItem();
      this.notifyIcon1 = new System.Windows.Forms.NotifyIcon(this.components);
      this.saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
      this.openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
      this.backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
      this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
      this.toolTipZxcvbnWarning = new System.Windows.Forms.ToolTip(this.components);
      this.toolTipZxcvbnSuggestions = new System.Windows.Forms.ToolTip(this.components);
      this.saveFileDialog2 = new System.Windows.Forms.SaveFileDialog();
      this.statusStrip1.SuspendLayout();
      this.menuStrip1.SuspendLayout();
      this.panelOuter.SuspendLayout();
      this.tabControl1.SuspendLayout();
      this.tabPageStartPage.SuspendLayout();
      this.panelStartPage.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxOption)).BeginInit();
      this.panelSidebarStart.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpap)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxDec)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsa)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxAtc)).BeginInit();
      this.tabPageEncrypt.SuspendLayout();
      this.panelEncrypt.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPassStrengthMeter)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrengthEmpty)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength04)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength03)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength02)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength01)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength00)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxEncryptBackToMain)).BeginInit();
      this.panelSidebarEncrypt.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxEncryption)).BeginInit();
      this.tabPageEncryptConfirm.SuspendLayout();
      this.panelEncryptConfirm.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPassStrengthMeterConfirm)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxEncryptConfirmBackButton)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCheckPasswordValidation)).BeginInit();
      this.panelSidebarEncryptConfirm.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxEncryptionConfirm)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxInValidIcon)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxValidIcon)).BeginInit();
      this.tabPageDecrypt.SuspendLayout();
      this.panelDecrypt.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxDecryptBackButton)).BeginInit();
      this.panelSidebarDecrypt.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxDecryption)).BeginInit();
      this.tabPageRsa.SuspendLayout();
      this.panelRsa.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsaBackButton)).BeginInit();
      this.panelHeaderRsa.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsaPage)).BeginInit();
      this.tabPageRsaKey.SuspendLayout();
      this.panelRsaKey.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxInfoBalloon)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPublicAndPrivateKey)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPrivateKey)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPublicKey)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsaKeyBackButton)).BeginInit();
      this.panelHeaderRsaKey.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsaType)).BeginInit();
      this.tabPagePostPpapRegister.SuspendLayout();
      this.panelPostPpapRegister.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpapRegisterBackButton)).BeginInit();
      this.panelHeaderPostPpapRegister.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpapPage)).BeginInit();
      this.tabPagePostPpapMain.SuspendLayout();
      this.panelPostPpapMain.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxMailAddress)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxInvalid)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxKey)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxLockIcon)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCheckIcon)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxValidDetails)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxValid)).BeginInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpapBackButton)).BeginInit();
      this.panelHeaderPostPpapMain.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpapMain)).BeginInit();
      this.panelPublicDragAndDrop.SuspendLayout();
      this.tabPageProgressState.SuspendLayout();
      this.panelProgressState.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxProgressStateBackButton)).BeginInit();
      this.panelSidebarProgress.SuspendLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxProgress)).BeginInit();
      this.contextMenuStrip1.SuspendLayout();
      this.contextMenuStrip2.SuspendLayout();
      this.SuspendLayout();
      // 
      // statusStrip1
      // 
      this.statusStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
      this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabelDataVersion,
            this.toolStripStatusLabelEncryptionTime,
            this.toolStripStatusLabelLicense});
      resources.ApplyResources(this.statusStrip1, "statusStrip1");
      this.statusStrip1.Name = "statusStrip1";
      // 
      // toolStripStatusLabelDataVersion
      // 
      this.toolStripStatusLabelDataVersion.Name = "toolStripStatusLabelDataVersion";
      resources.ApplyResources(this.toolStripStatusLabelDataVersion, "toolStripStatusLabelDataVersion");
      // 
      // toolStripStatusLabelEncryptionTime
      // 
      this.toolStripStatusLabelEncryptionTime.Name = "toolStripStatusLabelEncryptionTime";
      resources.ApplyResources(this.toolStripStatusLabelEncryptionTime, "toolStripStatusLabelEncryptionTime");
      // 
      // toolStripStatusLabelLicense
      // 
      this.toolStripStatusLabelLicense.ForeColor = System.Drawing.Color.DimGray;
      this.toolStripStatusLabelLicense.Name = "toolStripStatusLabelLicense";
      resources.ApplyResources(this.toolStripStatusLabelLicense, "toolStripStatusLabelLicense");
      this.toolStripStatusLabelLicense.Click += new System.EventHandler(this.toolStripStatusLabelLicense_Click);
      // 
      // menuStrip1
      // 
      this.menuStrip1.AllowDrop = true;
      this.menuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
      this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ToolStripMenuItemFile,
            this.ToolStripMenuItemOption,
            this.ToolStripMenuItemHelp});
      resources.ApplyResources(this.menuStrip1, "menuStrip1");
      this.menuStrip1.Name = "menuStrip1";
      // 
      // ToolStripMenuItemFile
      // 
      this.ToolStripMenuItemFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ToolStripMenuItemEncrypt,
            this.ToolStripMenuItemDecrypt,
            this.toolStripMenuItem2,
            this.ToolStripMenuItemExit});
      this.ToolStripMenuItemFile.Name = "ToolStripMenuItemFile";
      resources.ApplyResources(this.ToolStripMenuItemFile, "ToolStripMenuItemFile");
      this.ToolStripMenuItemFile.DropDownOpened += new System.EventHandler(this.ToolStripMenuItemFile_DropDownOpened);
      // 
      // ToolStripMenuItemEncrypt
      // 
      this.ToolStripMenuItemEncrypt.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ToolStripMenuItemEncryptSelectFiles,
            this.ToolStripMenuItemEncryptSelectFolder});
      resources.ApplyResources(this.ToolStripMenuItemEncrypt, "ToolStripMenuItemEncrypt");
      this.ToolStripMenuItemEncrypt.Name = "ToolStripMenuItemEncrypt";
      // 
      // ToolStripMenuItemEncryptSelectFiles
      // 
      resources.ApplyResources(this.ToolStripMenuItemEncryptSelectFiles, "ToolStripMenuItemEncryptSelectFiles");
      this.ToolStripMenuItemEncryptSelectFiles.Name = "ToolStripMenuItemEncryptSelectFiles";
      this.ToolStripMenuItemEncryptSelectFiles.Click += new System.EventHandler(this.ToolStripMenuItemEncryptSelectFiles_Click);
      // 
      // ToolStripMenuItemEncryptSelectFolder
      // 
      resources.ApplyResources(this.ToolStripMenuItemEncryptSelectFolder, "ToolStripMenuItemEncryptSelectFolder");
      this.ToolStripMenuItemEncryptSelectFolder.Name = "ToolStripMenuItemEncryptSelectFolder";
      this.ToolStripMenuItemEncryptSelectFolder.Click += new System.EventHandler(this.ToolStripMenuItemEncryptSelectFolder_Click);
      // 
      // ToolStripMenuItemDecrypt
      // 
      resources.ApplyResources(this.ToolStripMenuItemDecrypt, "ToolStripMenuItemDecrypt");
      this.ToolStripMenuItemDecrypt.Name = "ToolStripMenuItemDecrypt";
      this.ToolStripMenuItemDecrypt.Click += new System.EventHandler(this.ToolStripMenuItemDecrypt_Click);
      // 
      // toolStripMenuItem2
      // 
      this.toolStripMenuItem2.Name = "toolStripMenuItem2";
      resources.ApplyResources(this.toolStripMenuItem2, "toolStripMenuItem2");
      // 
      // ToolStripMenuItemExit
      // 
      this.ToolStripMenuItemExit.Name = "ToolStripMenuItemExit";
      resources.ApplyResources(this.ToolStripMenuItemExit, "ToolStripMenuItemExit");
      this.ToolStripMenuItemExit.Click += new System.EventHandler(this.ToolStripMenuItemExit_Click);
      // 
      // ToolStripMenuItemOption
      // 
      this.ToolStripMenuItemOption.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ToolStripMenuItemSetting});
      this.ToolStripMenuItemOption.Name = "ToolStripMenuItemOption";
      resources.ApplyResources(this.ToolStripMenuItemOption, "ToolStripMenuItemOption");
      this.ToolStripMenuItemOption.DropDownOpened += new System.EventHandler(this.ToolStripMenuItemOption_DropDownOpened);
      // 
      // ToolStripMenuItemSetting
      // 
      resources.ApplyResources(this.ToolStripMenuItemSetting, "ToolStripMenuItemSetting");
      this.ToolStripMenuItemSetting.Name = "ToolStripMenuItemSetting";
      this.ToolStripMenuItemSetting.Click += new System.EventHandler(this.ToolStripMenuItemSetting_Click);
      // 
      // ToolStripMenuItemHelp
      // 
      this.ToolStripMenuItemHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ToolStripMenuItemHelpContents,
            this.toolStripMenuItem1,
            this.ToolStripMenuItemAbout});
      this.ToolStripMenuItemHelp.Name = "ToolStripMenuItemHelp";
      resources.ApplyResources(this.ToolStripMenuItemHelp, "ToolStripMenuItemHelp");
      // 
      // ToolStripMenuItemHelpContents
      // 
      resources.ApplyResources(this.ToolStripMenuItemHelpContents, "ToolStripMenuItemHelpContents");
      this.ToolStripMenuItemHelpContents.Name = "ToolStripMenuItemHelpContents";
      this.ToolStripMenuItemHelpContents.Click += new System.EventHandler(this.ToolStripMenuItemHelpContents_Click);
      // 
      // toolStripMenuItem1
      // 
      this.toolStripMenuItem1.Name = "toolStripMenuItem1";
      resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
      // 
      // ToolStripMenuItemAbout
      // 
      this.ToolStripMenuItemAbout.Name = "ToolStripMenuItemAbout";
      resources.ApplyResources(this.ToolStripMenuItemAbout, "ToolStripMenuItemAbout");
      this.ToolStripMenuItemAbout.Click += new System.EventHandler(this.ToolStripMenuItemAbout_Click);
      // 
      // panelOuter
      // 
      this.panelOuter.BackColor = System.Drawing.SystemColors.Control;
      this.panelOuter.Controls.Add(this.tabControl1);
      resources.ApplyResources(this.panelOuter, "panelOuter");
      this.panelOuter.Name = "panelOuter";
      // 
      // tabControl1
      // 
      this.tabControl1.AllowDrop = true;
      this.tabControl1.Controls.Add(this.tabPageStartPage);
      this.tabControl1.Controls.Add(this.tabPageEncrypt);
      this.tabControl1.Controls.Add(this.tabPageEncryptConfirm);
      this.tabControl1.Controls.Add(this.tabPageDecrypt);
      this.tabControl1.Controls.Add(this.tabPageRsa);
      this.tabControl1.Controls.Add(this.tabPageRsaKey);
      this.tabControl1.Controls.Add(this.tabPagePostPpapRegister);
      this.tabControl1.Controls.Add(this.tabPagePostPpapMain);
      this.tabControl1.Controls.Add(this.tabPageProgressState);
      resources.ApplyResources(this.tabControl1, "tabControl1");
      this.tabControl1.Name = "tabControl1";
      this.tabControl1.SelectedIndex = 0;
      this.tabControl1.TabStop = false;
      this.tabControl1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.tabControl1.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // tabPageStartPage
      // 
      this.tabPageStartPage.BackColor = System.Drawing.SystemColors.Control;
      this.tabPageStartPage.Controls.Add(this.panelStartPage);
      resources.ApplyResources(this.tabPageStartPage, "tabPageStartPage");
      this.tabPageStartPage.Name = "tabPageStartPage";
      // 
      // panelStartPage
      // 
      this.panelStartPage.BackColor = System.Drawing.Color.Transparent;
      this.panelStartPage.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
      this.panelStartPage.Controls.Add(this.pictureBoxOption);
      this.panelStartPage.Controls.Add(this.buttonExit);
      this.panelStartPage.Controls.Add(this.panelSidebarStart);
      this.panelStartPage.Controls.Add(this.labelDragAndDrop);
      resources.ApplyResources(this.panelStartPage, "panelStartPage");
      this.panelStartPage.Name = "panelStartPage";
      this.panelStartPage.VisibleChanged += new System.EventHandler(this.panelStartPage_VisibleChanged);
      this.panelStartPage.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.panelStartPage.MouseEnter += new System.EventHandler(this.panelStartPage_MouseEnter);
      this.panelStartPage.MouseLeave += new System.EventHandler(this.panelStartPage_MouseLeave);
      this.panelStartPage.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // pictureBoxOption
      // 
      this.pictureBoxOption.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxOption.Cursor = System.Windows.Forms.Cursors.Hand;
      resources.ApplyResources(this.pictureBoxOption, "pictureBoxOption");
      this.pictureBoxOption.Name = "pictureBoxOption";
      this.pictureBoxOption.TabStop = false;
      this.pictureBoxOption.Click += new System.EventHandler(this.pictureBoxOption_Click);
      this.pictureBoxOption.MouseEnter += new System.EventHandler(this.pictureBoxOption_MouseEnter);
      this.pictureBoxOption.MouseLeave += new System.EventHandler(this.pictureBoxOption_MouseLeave);
      // 
      // buttonExit
      // 
      resources.ApplyResources(this.buttonExit, "buttonExit");
      this.buttonExit.Name = "buttonExit";
      this.buttonExit.UseVisualStyleBackColor = true;
      this.buttonExit.Click += new System.EventHandler(this.buttonExit_Click);
      // 
      // panelSidebarStart
      // 
      this.panelSidebarStart.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelSidebarStart.Controls.Add(this.labelPostPpap);
      this.panelSidebarStart.Controls.Add(this.pictureBoxPostPpap);
      this.panelSidebarStart.Controls.Add(this.labelDec);
      this.panelSidebarStart.Controls.Add(this.pictureBoxDec);
      this.panelSidebarStart.Controls.Add(this.labelRsa);
      this.panelSidebarStart.Controls.Add(this.pictureBoxRsa);
      this.panelSidebarStart.Controls.Add(this.labelAtc);
      this.panelSidebarStart.Controls.Add(this.pictureBoxAtc);
      resources.ApplyResources(this.panelSidebarStart, "panelSidebarStart");
      this.panelSidebarStart.Name = "panelSidebarStart";
      // 
      // labelPostPpap
      // 
      this.labelPostPpap.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.labelPostPpap, "labelPostPpap");
      this.labelPostPpap.Name = "labelPostPpap";
      // 
      // pictureBoxPostPpap
      // 
      this.pictureBoxPostPpap.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.pictureBoxPostPpap, "pictureBoxPostPpap");
      this.pictureBoxPostPpap.Name = "pictureBoxPostPpap";
      this.pictureBoxPostPpap.TabStop = false;
      this.pictureBoxPostPpap.Click += new System.EventHandler(this.pictureBoxPostPpap_Click);
      this.pictureBoxPostPpap.MouseEnter += new System.EventHandler(this.pictureBoxPostPpap_MouseEnter);
      this.pictureBoxPostPpap.MouseLeave += new System.EventHandler(this.pictureBoxPostPpap_MouseLeave);
      // 
      // labelDec
      // 
      this.labelDec.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.labelDec, "labelDec");
      this.labelDec.Name = "labelDec";
      // 
      // pictureBoxDec
      // 
      this.pictureBoxDec.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.pictureBoxDec, "pictureBoxDec");
      this.pictureBoxDec.Name = "pictureBoxDec";
      this.pictureBoxDec.TabStop = false;
      this.pictureBoxDec.Click += new System.EventHandler(this.pictureBoxDec_Click);
      this.pictureBoxDec.MouseEnter += new System.EventHandler(this.pictureBoxDec_MouseEnter);
      this.pictureBoxDec.MouseLeave += new System.EventHandler(this.pictureBoxDec_MouseLeave);
      // 
      // labelRsa
      // 
      this.labelRsa.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.labelRsa, "labelRsa");
      this.labelRsa.Name = "labelRsa";
      // 
      // pictureBoxRsa
      // 
      this.pictureBoxRsa.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.pictureBoxRsa, "pictureBoxRsa");
      this.pictureBoxRsa.Name = "pictureBoxRsa";
      this.pictureBoxRsa.TabStop = false;
      this.pictureBoxRsa.Click += new System.EventHandler(this.pictureBoxRsa_Click);
      this.pictureBoxRsa.MouseEnter += new System.EventHandler(this.pictureBoxRsa_MouseEnter);
      this.pictureBoxRsa.MouseLeave += new System.EventHandler(this.pictureBoxRsa_MouseLeave);
      // 
      // labelAtc
      // 
      this.labelAtc.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.labelAtc, "labelAtc");
      this.labelAtc.Name = "labelAtc";
      // 
      // pictureBoxAtc
      // 
      this.pictureBoxAtc.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.pictureBoxAtc, "pictureBoxAtc");
      this.pictureBoxAtc.Name = "pictureBoxAtc";
      this.pictureBoxAtc.TabStop = false;
      this.pictureBoxAtc.Click += new System.EventHandler(this.pictureBoxAtc_Click);
      this.pictureBoxAtc.MouseEnter += new System.EventHandler(this.pictureBoxAtc_MouseEnter);
      this.pictureBoxAtc.MouseLeave += new System.EventHandler(this.pictureBoxAtc_MouseLeave);
      // 
      // labelDragAndDrop
      // 
      resources.ApplyResources(this.labelDragAndDrop, "labelDragAndDrop");
      this.labelDragAndDrop.Name = "labelDragAndDrop";
      this.labelDragAndDrop.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.labelDragAndDrop.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // tabPageEncrypt
      // 
      this.tabPageEncrypt.BackColor = System.Drawing.SystemColors.Control;
      this.tabPageEncrypt.Controls.Add(this.panelEncrypt);
      resources.ApplyResources(this.tabPageEncrypt, "tabPageEncrypt");
      this.tabPageEncrypt.Name = "tabPageEncrypt";
      // 
      // panelEncrypt
      // 
      this.panelEncrypt.BackColor = System.Drawing.Color.Transparent;
      this.panelEncrypt.Controls.Add(this.labelPasswordStrength);
      this.panelEncrypt.Controls.Add(this.checkBoxDeleteOriginalFileAfterEncryption);
      this.panelEncrypt.Controls.Add(this.pictureBoxPassStrengthMeter);
      this.panelEncrypt.Controls.Add(this.pictureBoxPasswordStrengthEmpty);
      this.panelEncrypt.Controls.Add(this.pictureBoxPasswordStrength04);
      this.panelEncrypt.Controls.Add(this.pictureBoxPasswordStrength03);
      this.panelEncrypt.Controls.Add(this.pictureBoxPasswordStrength02);
      this.panelEncrypt.Controls.Add(this.pictureBoxPasswordStrength01);
      this.panelEncrypt.Controls.Add(this.pictureBoxPasswordStrength00);
      this.panelEncrypt.Controls.Add(this.pictureBoxEncryptBackToMain);
      this.panelEncrypt.Controls.Add(this.buttonEncryptionPasswordOk);
      this.panelEncrypt.Controls.Add(this.panelSidebarEncrypt);
      this.panelEncrypt.Controls.Add(this.buttonEncryptCancel);
      this.panelEncrypt.Controls.Add(this.labelPassword);
      this.panelEncrypt.Controls.Add(this.textBoxPassword);
      resources.ApplyResources(this.panelEncrypt, "panelEncrypt");
      this.panelEncrypt.Name = "panelEncrypt";
      this.panelEncrypt.VisibleChanged += new System.EventHandler(this.panelEncrypt_VisibleChanged);
      this.panelEncrypt.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.panelEncrypt.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // labelPasswordStrength
      // 
      resources.ApplyResources(this.labelPasswordStrength, "labelPasswordStrength");
      this.labelPasswordStrength.ForeColor = System.Drawing.Color.Gray;
      this.labelPasswordStrength.Name = "labelPasswordStrength";
      this.labelPasswordStrength.TextChanged += new System.EventHandler(this.labelPasswordStrength_TextChanged);
      // 
      // checkBoxDeleteOriginalFileAfterEncryption
      // 
      resources.ApplyResources(this.checkBoxDeleteOriginalFileAfterEncryption, "checkBoxDeleteOriginalFileAfterEncryption");
      this.checkBoxDeleteOriginalFileAfterEncryption.Name = "checkBoxDeleteOriginalFileAfterEncryption";
      this.checkBoxDeleteOriginalFileAfterEncryption.UseVisualStyleBackColor = true;
      this.checkBoxDeleteOriginalFileAfterEncryption.CheckedChanged += new System.EventHandler(this.checkBoxDeleteOriginalFileAfterEncryption_CheckedChanged);
      // 
      // pictureBoxPassStrengthMeter
      // 
      resources.ApplyResources(this.pictureBoxPassStrengthMeter, "pictureBoxPassStrengthMeter");
      this.pictureBoxPassStrengthMeter.Name = "pictureBoxPassStrengthMeter";
      this.pictureBoxPassStrengthMeter.TabStop = false;
      this.pictureBoxPassStrengthMeter.LocationChanged += new System.EventHandler(this.pictureBoxPassStrengthMeter_LocationChanged);
      // 
      // pictureBoxPasswordStrengthEmpty
      // 
      resources.ApplyResources(this.pictureBoxPasswordStrengthEmpty, "pictureBoxPasswordStrengthEmpty");
      this.pictureBoxPasswordStrengthEmpty.Name = "pictureBoxPasswordStrengthEmpty";
      this.pictureBoxPasswordStrengthEmpty.TabStop = false;
      // 
      // pictureBoxPasswordStrength04
      // 
      resources.ApplyResources(this.pictureBoxPasswordStrength04, "pictureBoxPasswordStrength04");
      this.pictureBoxPasswordStrength04.Name = "pictureBoxPasswordStrength04";
      this.pictureBoxPasswordStrength04.TabStop = false;
      // 
      // pictureBoxPasswordStrength03
      // 
      resources.ApplyResources(this.pictureBoxPasswordStrength03, "pictureBoxPasswordStrength03");
      this.pictureBoxPasswordStrength03.Name = "pictureBoxPasswordStrength03";
      this.pictureBoxPasswordStrength03.TabStop = false;
      // 
      // pictureBoxPasswordStrength02
      // 
      resources.ApplyResources(this.pictureBoxPasswordStrength02, "pictureBoxPasswordStrength02");
      this.pictureBoxPasswordStrength02.Name = "pictureBoxPasswordStrength02";
      this.pictureBoxPasswordStrength02.TabStop = false;
      // 
      // pictureBoxPasswordStrength01
      // 
      resources.ApplyResources(this.pictureBoxPasswordStrength01, "pictureBoxPasswordStrength01");
      this.pictureBoxPasswordStrength01.Name = "pictureBoxPasswordStrength01";
      this.pictureBoxPasswordStrength01.TabStop = false;
      // 
      // pictureBoxPasswordStrength00
      // 
      resources.ApplyResources(this.pictureBoxPasswordStrength00, "pictureBoxPasswordStrength00");
      this.pictureBoxPasswordStrength00.Name = "pictureBoxPasswordStrength00";
      this.pictureBoxPasswordStrength00.TabStop = false;
      // 
      // pictureBoxEncryptBackToMain
      // 
      resources.ApplyResources(this.pictureBoxEncryptBackToMain, "pictureBoxEncryptBackToMain");
      this.pictureBoxEncryptBackToMain.Cursor = System.Windows.Forms.Cursors.Hand;
      this.pictureBoxEncryptBackToMain.Name = "pictureBoxEncryptBackToMain";
      this.pictureBoxEncryptBackToMain.TabStop = false;
      this.pictureBoxEncryptBackToMain.Click += new System.EventHandler(this.buttonEncryptCancel_Click);
      this.pictureBoxEncryptBackToMain.MouseEnter += new System.EventHandler(this.pictureBoxEncryptBackButton_MouseEnter);
      this.pictureBoxEncryptBackToMain.MouseLeave += new System.EventHandler(this.pictureBoxEncryptBackButton_MouseLeave);
      // 
      // buttonEncryptionPasswordOk
      // 
      resources.ApplyResources(this.buttonEncryptionPasswordOk, "buttonEncryptionPasswordOk");
      this.buttonEncryptionPasswordOk.Name = "buttonEncryptionPasswordOk";
      this.buttonEncryptionPasswordOk.UseVisualStyleBackColor = true;
      this.buttonEncryptionPasswordOk.Click += new System.EventHandler(this.buttonEncryptionPasswordOk_Click);
      // 
      // panelSidebarEncrypt
      // 
      this.panelSidebarEncrypt.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelSidebarEncrypt.Controls.Add(this.labelEncryption);
      this.panelSidebarEncrypt.Controls.Add(this.pictureBoxEncryption);
      resources.ApplyResources(this.panelSidebarEncrypt, "panelSidebarEncrypt");
      this.panelSidebarEncrypt.Name = "panelSidebarEncrypt";
      // 
      // labelEncryption
      // 
      resources.ApplyResources(this.labelEncryption, "labelEncryption");
      this.labelEncryption.BackColor = System.Drawing.Color.Transparent;
      this.labelEncryption.Name = "labelEncryption";
      // 
      // pictureBoxEncryption
      // 
      resources.ApplyResources(this.pictureBoxEncryption, "pictureBoxEncryption");
      this.pictureBoxEncryption.Cursor = System.Windows.Forms.Cursors.Hand;
      this.pictureBoxEncryption.Name = "pictureBoxEncryption";
      this.pictureBoxEncryption.TabStop = false;
      this.pictureBoxEncryption.Click += new System.EventHandler(this.pictureBoxEncryption_Click);
      // 
      // buttonEncryptCancel
      // 
      resources.ApplyResources(this.buttonEncryptCancel, "buttonEncryptCancel");
      this.buttonEncryptCancel.Name = "buttonEncryptCancel";
      this.buttonEncryptCancel.UseVisualStyleBackColor = true;
      this.buttonEncryptCancel.Click += new System.EventHandler(this.buttonEncryptCancel_Click);
      // 
      // labelPassword
      // 
      resources.ApplyResources(this.labelPassword, "labelPassword");
      this.labelPassword.BackColor = System.Drawing.Color.Transparent;
      this.labelPassword.Name = "labelPassword";
      this.labelPassword.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.labelPassword.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // textBoxPassword
      // 
      resources.ApplyResources(this.textBoxPassword, "textBoxPassword");
      this.textBoxPassword.Name = "textBoxPassword";
      this.textBoxPassword.IsPasswordVisibleChanged += new System.EventHandler(this.textBoxPassword_IsPasswordVisibleChanged);
      this.textBoxPassword.TextChanged += new System.EventHandler(this.textBoxPassword_TextChanged);
      this.textBoxPassword.DragDrop += new System.Windows.Forms.DragEventHandler(this.textBoxPassword_DragDrop);
      this.textBoxPassword.DragEnter += new System.Windows.Forms.DragEventHandler(this.textBoxPassword_DragEnter);
      this.textBoxPassword.DragLeave += new System.EventHandler(this.textBoxPassword_DragLeave);
      this.textBoxPassword.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textBoxPassword_KeyDown);
      // 
      // tabPageEncryptConfirm
      // 
      this.tabPageEncryptConfirm.BackColor = System.Drawing.Color.White;
      this.tabPageEncryptConfirm.Controls.Add(this.panelEncryptConfirm);
      resources.ApplyResources(this.tabPageEncryptConfirm, "tabPageEncryptConfirm");
      this.tabPageEncryptConfirm.Name = "tabPageEncryptConfirm";
      // 
      // panelEncryptConfirm
      // 
      this.panelEncryptConfirm.BackColor = System.Drawing.Color.Transparent;
      this.panelEncryptConfirm.Controls.Add(this.labelPasswordStrengthConfirm);
      this.panelEncryptConfirm.Controls.Add(this.pictureBoxPassStrengthMeterConfirm);
      this.panelEncryptConfirm.Controls.Add(this.textBoxRePassword);
      this.panelEncryptConfirm.Controls.Add(this.pictureBoxEncryptConfirmBackButton);
      this.panelEncryptConfirm.Controls.Add(this.pictureBoxCheckPasswordValidation);
      this.panelEncryptConfirm.Controls.Add(this.panelSidebarEncryptConfirm);
      this.panelEncryptConfirm.Controls.Add(this.buttonEncryptionConfirmCancel);
      this.panelEncryptConfirm.Controls.Add(this.buttonEncryptStart);
      this.panelEncryptConfirm.Controls.Add(this.pictureBoxInValidIcon);
      this.panelEncryptConfirm.Controls.Add(this.pictureBoxValidIcon);
      this.panelEncryptConfirm.Controls.Add(this.checkBoxReDeleteOriginalFileAfterEncryption);
      this.panelEncryptConfirm.Controls.Add(this.labelInputPasswordAgain);
      resources.ApplyResources(this.panelEncryptConfirm, "panelEncryptConfirm");
      this.panelEncryptConfirm.Name = "panelEncryptConfirm";
      this.panelEncryptConfirm.VisibleChanged += new System.EventHandler(this.panelEncryptConfirm_VisibleChanged);
      this.panelEncryptConfirm.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.panelEncryptConfirm.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // labelPasswordStrengthConfirm
      // 
      resources.ApplyResources(this.labelPasswordStrengthConfirm, "labelPasswordStrengthConfirm");
      this.labelPasswordStrengthConfirm.ForeColor = System.Drawing.Color.Gray;
      this.labelPasswordStrengthConfirm.Name = "labelPasswordStrengthConfirm";
      // 
      // pictureBoxPassStrengthMeterConfirm
      // 
      resources.ApplyResources(this.pictureBoxPassStrengthMeterConfirm, "pictureBoxPassStrengthMeterConfirm");
      this.pictureBoxPassStrengthMeterConfirm.Name = "pictureBoxPassStrengthMeterConfirm";
      this.pictureBoxPassStrengthMeterConfirm.TabStop = false;
      // 
      // textBoxRePassword
      // 
      this.textBoxRePassword.BackColor = System.Drawing.Color.PapayaWhip;
      resources.ApplyResources(this.textBoxRePassword, "textBoxRePassword");
      this.textBoxRePassword.Name = "textBoxRePassword";
      this.textBoxRePassword.TextChanged += new System.EventHandler(this.textBoxRePassword_TextChanged);
      this.textBoxRePassword.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textBoxRePassword_KeyDown);
      // 
      // pictureBoxEncryptConfirmBackButton
      // 
      resources.ApplyResources(this.pictureBoxEncryptConfirmBackButton, "pictureBoxEncryptConfirmBackButton");
      this.pictureBoxEncryptConfirmBackButton.Cursor = System.Windows.Forms.Cursors.Hand;
      this.pictureBoxEncryptConfirmBackButton.Name = "pictureBoxEncryptConfirmBackButton";
      this.pictureBoxEncryptConfirmBackButton.TabStop = false;
      this.pictureBoxEncryptConfirmBackButton.Click += new System.EventHandler(this.buttonEncryptionConfirmCancel_Click);
      this.pictureBoxEncryptConfirmBackButton.MouseEnter += new System.EventHandler(this.pictureBoxEncryptConfirmBackButton_MouseEnter);
      this.pictureBoxEncryptConfirmBackButton.MouseLeave += new System.EventHandler(this.pictureBoxEncryptConfirmBackButton_MouseLeave);
      // 
      // pictureBoxCheckPasswordValidation
      // 
      resources.ApplyResources(this.pictureBoxCheckPasswordValidation, "pictureBoxCheckPasswordValidation");
      this.pictureBoxCheckPasswordValidation.Name = "pictureBoxCheckPasswordValidation";
      this.pictureBoxCheckPasswordValidation.TabStop = false;
      // 
      // panelSidebarEncryptConfirm
      // 
      this.panelSidebarEncryptConfirm.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelSidebarEncryptConfirm.Controls.Add(this.labelEncryptionConfirm);
      this.panelSidebarEncryptConfirm.Controls.Add(this.pictureBoxEncryptionConfirm);
      resources.ApplyResources(this.panelSidebarEncryptConfirm, "panelSidebarEncryptConfirm");
      this.panelSidebarEncryptConfirm.Name = "panelSidebarEncryptConfirm";
      // 
      // labelEncryptionConfirm
      // 
      resources.ApplyResources(this.labelEncryptionConfirm, "labelEncryptionConfirm");
      this.labelEncryptionConfirm.BackColor = System.Drawing.Color.Transparent;
      this.labelEncryptionConfirm.Name = "labelEncryptionConfirm";
      // 
      // pictureBoxEncryptionConfirm
      // 
      resources.ApplyResources(this.pictureBoxEncryptionConfirm, "pictureBoxEncryptionConfirm");
      this.pictureBoxEncryptionConfirm.Cursor = System.Windows.Forms.Cursors.Hand;
      this.pictureBoxEncryptionConfirm.Name = "pictureBoxEncryptionConfirm";
      this.pictureBoxEncryptionConfirm.TabStop = false;
      // 
      // buttonEncryptionConfirmCancel
      // 
      resources.ApplyResources(this.buttonEncryptionConfirmCancel, "buttonEncryptionConfirmCancel");
      this.buttonEncryptionConfirmCancel.Name = "buttonEncryptionConfirmCancel";
      this.buttonEncryptionConfirmCancel.UseVisualStyleBackColor = true;
      this.buttonEncryptionConfirmCancel.Click += new System.EventHandler(this.buttonEncryptionConfirmCancel_Click);
      // 
      // buttonEncryptStart
      // 
      resources.ApplyResources(this.buttonEncryptStart, "buttonEncryptStart");
      this.buttonEncryptStart.Name = "buttonEncryptStart";
      this.buttonEncryptStart.UseVisualStyleBackColor = true;
      this.buttonEncryptStart.Click += new System.EventHandler(this.buttonEncryptStart_Click);
      // 
      // pictureBoxInValidIcon
      // 
      resources.ApplyResources(this.pictureBoxInValidIcon, "pictureBoxInValidIcon");
      this.pictureBoxInValidIcon.Name = "pictureBoxInValidIcon";
      this.pictureBoxInValidIcon.TabStop = false;
      // 
      // pictureBoxValidIcon
      // 
      resources.ApplyResources(this.pictureBoxValidIcon, "pictureBoxValidIcon");
      this.pictureBoxValidIcon.Name = "pictureBoxValidIcon";
      this.pictureBoxValidIcon.TabStop = false;
      // 
      // checkBoxReDeleteOriginalFileAfterEncryption
      // 
      resources.ApplyResources(this.checkBoxReDeleteOriginalFileAfterEncryption, "checkBoxReDeleteOriginalFileAfterEncryption");
      this.checkBoxReDeleteOriginalFileAfterEncryption.Name = "checkBoxReDeleteOriginalFileAfterEncryption";
      this.checkBoxReDeleteOriginalFileAfterEncryption.UseVisualStyleBackColor = true;
      this.checkBoxReDeleteOriginalFileAfterEncryption.CheckedChanged += new System.EventHandler(this.checkBoxReDeleteOriginalFileAfterEncryption_CheckedChanged);
      // 
      // labelInputPasswordAgain
      // 
      resources.ApplyResources(this.labelInputPasswordAgain, "labelInputPasswordAgain");
      this.labelInputPasswordAgain.BackColor = System.Drawing.Color.Transparent;
      this.labelInputPasswordAgain.Name = "labelInputPasswordAgain";
      // 
      // tabPageDecrypt
      // 
      this.tabPageDecrypt.BackColor = System.Drawing.SystemColors.Control;
      this.tabPageDecrypt.Controls.Add(this.panelDecrypt);
      resources.ApplyResources(this.tabPageDecrypt, "tabPageDecrypt");
      this.tabPageDecrypt.Name = "tabPageDecrypt";
      // 
      // panelDecrypt
      // 
      this.panelDecrypt.BackColor = System.Drawing.Color.Transparent;
      this.panelDecrypt.Controls.Add(this.roundedBorderLabelSalvageMode);
      this.panelDecrypt.Controls.Add(this.textBoxDecryptPassword);
      this.panelDecrypt.Controls.Add(this.pictureBoxDecryptBackButton);
      this.panelDecrypt.Controls.Add(this.panelSidebarDecrypt);
      this.panelDecrypt.Controls.Add(this.checkBoxDeleteAtcFileAfterDecryption);
      this.panelDecrypt.Controls.Add(this.buttonDecryptCancel);
      this.panelDecrypt.Controls.Add(this.buttonDecryptStart);
      this.panelDecrypt.Controls.Add(this.labelDecryptionPassword);
      resources.ApplyResources(this.panelDecrypt, "panelDecrypt");
      this.panelDecrypt.Name = "panelDecrypt";
      this.panelDecrypt.VisibleChanged += new System.EventHandler(this.panelDecrypt_VisibleChanged);
      this.panelDecrypt.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.panelDecrypt.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // roundedBorderLabelSalvageMode
      // 
      resources.ApplyResources(this.roundedBorderLabelSalvageMode, "roundedBorderLabelSalvageMode");
      this.roundedBorderLabelSalvageMode.BackColor = System.Drawing.Color.Transparent;
      this.roundedBorderLabelSalvageMode.BorderColor = System.Drawing.Color.DarkGreen;
      this.roundedBorderLabelSalvageMode.BorderThickness = 2;
      this.roundedBorderLabelSalvageMode.CornerRadius = 6;
      this.roundedBorderLabelSalvageMode.ForeColor = System.Drawing.Color.DarkGreen;
      this.roundedBorderLabelSalvageMode.Name = "roundedBorderLabelSalvageMode";
      // 
      // textBoxDecryptPassword
      // 
      resources.ApplyResources(this.textBoxDecryptPassword, "textBoxDecryptPassword");
      this.textBoxDecryptPassword.Name = "textBoxDecryptPassword";
      this.textBoxDecryptPassword.TextChanged += new System.EventHandler(this.textBoxDecryptPassword_TextChanged);
      this.textBoxDecryptPassword.DragDrop += new System.Windows.Forms.DragEventHandler(this.textBoxDecryptPassword_DragDrop);
      this.textBoxDecryptPassword.DragEnter += new System.Windows.Forms.DragEventHandler(this.textBoxDecryptPassword_DragEnter);
      this.textBoxDecryptPassword.DragLeave += new System.EventHandler(this.textBoxDecryptPassword_DragLeave);
      this.textBoxDecryptPassword.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textBoxDecryptPassword_KeyDown);
      this.textBoxDecryptPassword.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.textBoxDecryptPassword_KeyPress);
      this.textBoxDecryptPassword.PreviewKeyDown += new System.Windows.Forms.PreviewKeyDownEventHandler(this.textBoxDecryptPassword_PreviewKeyDown);
      // 
      // pictureBoxDecryptBackButton
      // 
      resources.ApplyResources(this.pictureBoxDecryptBackButton, "pictureBoxDecryptBackButton");
      this.pictureBoxDecryptBackButton.Cursor = System.Windows.Forms.Cursors.Hand;
      this.pictureBoxDecryptBackButton.Name = "pictureBoxDecryptBackButton";
      this.pictureBoxDecryptBackButton.TabStop = false;
      this.pictureBoxDecryptBackButton.Click += new System.EventHandler(this.buttonDecryptCancel_Click);
      this.pictureBoxDecryptBackButton.MouseEnter += new System.EventHandler(this.pictureBoxDecryptBackButton_MouseEnter);
      this.pictureBoxDecryptBackButton.MouseLeave += new System.EventHandler(this.pictureBoxDecryptBackButton_MouseLeave);
      // 
      // panelSidebarDecrypt
      // 
      this.panelSidebarDecrypt.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelSidebarDecrypt.Controls.Add(this.labelDecryption);
      this.panelSidebarDecrypt.Controls.Add(this.pictureBoxDecryption);
      resources.ApplyResources(this.panelSidebarDecrypt, "panelSidebarDecrypt");
      this.panelSidebarDecrypt.Name = "panelSidebarDecrypt";
      // 
      // labelDecryption
      // 
      resources.ApplyResources(this.labelDecryption, "labelDecryption");
      this.labelDecryption.BackColor = System.Drawing.Color.Transparent;
      this.labelDecryption.Name = "labelDecryption";
      // 
      // pictureBoxDecryption
      // 
      resources.ApplyResources(this.pictureBoxDecryption, "pictureBoxDecryption");
      this.pictureBoxDecryption.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxDecryption.Name = "pictureBoxDecryption";
      this.pictureBoxDecryption.TabStop = false;
      // 
      // checkBoxDeleteAtcFileAfterDecryption
      // 
      resources.ApplyResources(this.checkBoxDeleteAtcFileAfterDecryption, "checkBoxDeleteAtcFileAfterDecryption");
      this.checkBoxDeleteAtcFileAfterDecryption.Name = "checkBoxDeleteAtcFileAfterDecryption";
      this.checkBoxDeleteAtcFileAfterDecryption.UseVisualStyleBackColor = true;
      this.checkBoxDeleteAtcFileAfterDecryption.CheckedChanged += new System.EventHandler(this.checkBoxDeleteAtcFileAfterDecryption_CheckedChanged);
      // 
      // buttonDecryptCancel
      // 
      resources.ApplyResources(this.buttonDecryptCancel, "buttonDecryptCancel");
      this.buttonDecryptCancel.Name = "buttonDecryptCancel";
      this.buttonDecryptCancel.UseVisualStyleBackColor = true;
      this.buttonDecryptCancel.Click += new System.EventHandler(this.buttonDecryptCancel_Click);
      // 
      // buttonDecryptStart
      // 
      resources.ApplyResources(this.buttonDecryptStart, "buttonDecryptStart");
      this.buttonDecryptStart.Name = "buttonDecryptStart";
      this.buttonDecryptStart.UseVisualStyleBackColor = true;
      this.buttonDecryptStart.Click += new System.EventHandler(this.buttonDecryptStart_Click);
      // 
      // labelDecryptionPassword
      // 
      resources.ApplyResources(this.labelDecryptionPassword, "labelDecryptionPassword");
      this.labelDecryptionPassword.Name = "labelDecryptionPassword";
      this.labelDecryptionPassword.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.labelDecryptionPassword.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // tabPageRsa
      // 
      this.tabPageRsa.Controls.Add(this.panelRsa);
      resources.ApplyResources(this.tabPageRsa, "tabPageRsa");
      this.tabPageRsa.Name = "tabPageRsa";
      this.tabPageRsa.UseVisualStyleBackColor = true;
      // 
      // panelRsa
      // 
      this.panelRsa.Controls.Add(this.pictureBoxRsaBackButton);
      this.panelRsa.Controls.Add(this.labelRsaMessage);
      this.panelRsa.Controls.Add(this.buttonRsaCancel);
      this.panelRsa.Controls.Add(this.buttonGenerateKey);
      this.panelRsa.Controls.Add(this.panelHeaderRsa);
      resources.ApplyResources(this.panelRsa, "panelRsa");
      this.panelRsa.Name = "panelRsa";
      this.panelRsa.VisibleChanged += new System.EventHandler(this.panelRsa_VisibleChanged);
      // 
      // pictureBoxRsaBackButton
      // 
      this.pictureBoxRsaBackButton.Cursor = System.Windows.Forms.Cursors.Hand;
      resources.ApplyResources(this.pictureBoxRsaBackButton, "pictureBoxRsaBackButton");
      this.pictureBoxRsaBackButton.Name = "pictureBoxRsaBackButton";
      this.pictureBoxRsaBackButton.TabStop = false;
      this.pictureBoxRsaBackButton.Click += new System.EventHandler(this.buttonRsaCancel_Click);
      this.pictureBoxRsaBackButton.MouseEnter += new System.EventHandler(this.pictureBoxRsaBackButton_MouseEnter);
      this.pictureBoxRsaBackButton.MouseLeave += new System.EventHandler(this.pictureBoxRsaBackButton_MouseLeave);
      // 
      // labelRsaMessage
      // 
      resources.ApplyResources(this.labelRsaMessage, "labelRsaMessage");
      this.labelRsaMessage.Name = "labelRsaMessage";
      // 
      // buttonRsaCancel
      // 
      resources.ApplyResources(this.buttonRsaCancel, "buttonRsaCancel");
      this.buttonRsaCancel.Name = "buttonRsaCancel";
      this.buttonRsaCancel.UseVisualStyleBackColor = true;
      this.buttonRsaCancel.Click += new System.EventHandler(this.buttonRsaCancel_Click);
      // 
      // buttonGenerateKey
      // 
      resources.ApplyResources(this.buttonGenerateKey, "buttonGenerateKey");
      this.buttonGenerateKey.Name = "buttonGenerateKey";
      this.buttonGenerateKey.UseVisualStyleBackColor = true;
      this.buttonGenerateKey.Click += new System.EventHandler(this.buttonGenerateKey_Click);
      // 
      // panelHeaderRsa
      // 
      this.panelHeaderRsa.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelHeaderRsa.Controls.Add(this.labelRsaDetail);
      this.panelHeaderRsa.Controls.Add(this.pictureBoxRsaPage);
      resources.ApplyResources(this.panelHeaderRsa, "panelHeaderRsa");
      this.panelHeaderRsa.Name = "panelHeaderRsa";
      // 
      // labelRsaDetail
      // 
      resources.ApplyResources(this.labelRsaDetail, "labelRsaDetail");
      this.labelRsaDetail.BackColor = System.Drawing.Color.Transparent;
      this.labelRsaDetail.Name = "labelRsaDetail";
      // 
      // pictureBoxRsaPage
      // 
      resources.ApplyResources(this.pictureBoxRsaPage, "pictureBoxRsaPage");
      this.pictureBoxRsaPage.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxRsaPage.Name = "pictureBoxRsaPage";
      this.pictureBoxRsaPage.TabStop = false;
      // 
      // tabPageRsaKey
      // 
      this.tabPageRsaKey.Controls.Add(this.panelRsaKey);
      resources.ApplyResources(this.tabPageRsaKey, "tabPageRsaKey");
      this.tabPageRsaKey.Name = "tabPageRsaKey";
      this.tabPageRsaKey.UseVisualStyleBackColor = true;
      // 
      // panelRsaKey
      // 
      this.panelRsaKey.Controls.Add(this.labelRsaInformation);
      this.panelRsaKey.Controls.Add(this.pictureBoxInfoBalloon);
      this.panelRsaKey.Controls.Add(this.textBoxHashString);
      this.panelRsaKey.Controls.Add(this.comboBoxHashList);
      this.panelRsaKey.Controls.Add(this.labelRsaDescription);
      this.panelRsaKey.Controls.Add(this.pictureBoxPublicAndPrivateKey);
      this.panelRsaKey.Controls.Add(this.pictureBoxPrivateKey);
      this.panelRsaKey.Controls.Add(this.pictureBoxPublicKey);
      this.panelRsaKey.Controls.Add(this.pictureBoxRsaKeyBackButton);
      this.panelRsaKey.Controls.Add(this.buttonRsaKeyCancel);
      this.panelRsaKey.Controls.Add(this.panelHeaderRsaKey);
      resources.ApplyResources(this.panelRsaKey, "panelRsaKey");
      this.panelRsaKey.Name = "panelRsaKey";
      this.panelRsaKey.VisibleChanged += new System.EventHandler(this.panelRsaKey_VisibleChanged);
      // 
      // labelRsaInformation
      // 
      resources.ApplyResources(this.labelRsaInformation, "labelRsaInformation");
      this.labelRsaInformation.Name = "labelRsaInformation";
      // 
      // pictureBoxInfoBalloon
      // 
      resources.ApplyResources(this.pictureBoxInfoBalloon, "pictureBoxInfoBalloon");
      this.pictureBoxInfoBalloon.Name = "pictureBoxInfoBalloon";
      this.pictureBoxInfoBalloon.TabStop = false;
      // 
      // textBoxHashString
      // 
      resources.ApplyResources(this.textBoxHashString, "textBoxHashString");
      this.textBoxHashString.Name = "textBoxHashString";
      this.textBoxHashString.ReadOnly = true;
      // 
      // comboBoxHashList
      // 
      resources.ApplyResources(this.comboBoxHashList, "comboBoxHashList");
      this.comboBoxHashList.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
      this.comboBoxHashList.FormattingEnabled = true;
      this.comboBoxHashList.Items.AddRange(new object[] {
            resources.GetString("comboBoxHashList.Items"),
            resources.GetString("comboBoxHashList.Items1"),
            resources.GetString("comboBoxHashList.Items2"),
            resources.GetString("comboBoxHashList.Items3")});
      this.comboBoxHashList.Name = "comboBoxHashList";
      this.comboBoxHashList.SelectedIndexChanged += new System.EventHandler(this.comboBoxHashList_SelectedIndexChanged);
      // 
      // labelRsaDescription
      // 
      resources.ApplyResources(this.labelRsaDescription, "labelRsaDescription");
      this.labelRsaDescription.Name = "labelRsaDescription";
      // 
      // pictureBoxPublicAndPrivateKey
      // 
      resources.ApplyResources(this.pictureBoxPublicAndPrivateKey, "pictureBoxPublicAndPrivateKey");
      this.pictureBoxPublicAndPrivateKey.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxPublicAndPrivateKey.Name = "pictureBoxPublicAndPrivateKey";
      this.pictureBoxPublicAndPrivateKey.TabStop = false;
      // 
      // pictureBoxPrivateKey
      // 
      resources.ApplyResources(this.pictureBoxPrivateKey, "pictureBoxPrivateKey");
      this.pictureBoxPrivateKey.Name = "pictureBoxPrivateKey";
      this.pictureBoxPrivateKey.TabStop = false;
      // 
      // pictureBoxPublicKey
      // 
      resources.ApplyResources(this.pictureBoxPublicKey, "pictureBoxPublicKey");
      this.pictureBoxPublicKey.Name = "pictureBoxPublicKey";
      this.pictureBoxPublicKey.TabStop = false;
      // 
      // pictureBoxRsaKeyBackButton
      // 
      resources.ApplyResources(this.pictureBoxRsaKeyBackButton, "pictureBoxRsaKeyBackButton");
      this.pictureBoxRsaKeyBackButton.Cursor = System.Windows.Forms.Cursors.Hand;
      this.pictureBoxRsaKeyBackButton.Name = "pictureBoxRsaKeyBackButton";
      this.pictureBoxRsaKeyBackButton.TabStop = false;
      this.pictureBoxRsaKeyBackButton.Click += new System.EventHandler(this.buttonRsaKeyCancel_Click);
      this.pictureBoxRsaKeyBackButton.MouseEnter += new System.EventHandler(this.pictureBoxRsaKeyBackButton_MouseEnter);
      this.pictureBoxRsaKeyBackButton.MouseLeave += new System.EventHandler(this.pictureBoxRsaKeyBackButton_MouseLeave);
      // 
      // buttonRsaKeyCancel
      // 
      resources.ApplyResources(this.buttonRsaKeyCancel, "buttonRsaKeyCancel");
      this.buttonRsaKeyCancel.Name = "buttonRsaKeyCancel";
      this.buttonRsaKeyCancel.UseVisualStyleBackColor = true;
      this.buttonRsaKeyCancel.Click += new System.EventHandler(this.buttonRsaKeyCancel_Click);
      // 
      // panelHeaderRsaKey
      // 
      this.panelHeaderRsaKey.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelHeaderRsaKey.Controls.Add(this.labelRsaKeyName);
      this.panelHeaderRsaKey.Controls.Add(this.pictureBoxRsaType);
      resources.ApplyResources(this.panelHeaderRsaKey, "panelHeaderRsaKey");
      this.panelHeaderRsaKey.Name = "panelHeaderRsaKey";
      // 
      // labelRsaKeyName
      // 
      resources.ApplyResources(this.labelRsaKeyName, "labelRsaKeyName");
      this.labelRsaKeyName.BackColor = System.Drawing.Color.Transparent;
      this.labelRsaKeyName.Name = "labelRsaKeyName";
      // 
      // pictureBoxRsaType
      // 
      resources.ApplyResources(this.pictureBoxRsaType, "pictureBoxRsaType");
      this.pictureBoxRsaType.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxRsaType.Name = "pictureBoxRsaType";
      this.pictureBoxRsaType.TabStop = false;
      // 
      // tabPagePostPpapRegister
      // 
      this.tabPagePostPpapRegister.Controls.Add(this.panelPostPpapRegister);
      resources.ApplyResources(this.tabPagePostPpapRegister, "tabPagePostPpapRegister");
      this.tabPagePostPpapRegister.Name = "tabPagePostPpapRegister";
      this.tabPagePostPpapRegister.UseVisualStyleBackColor = true;
      // 
      // panelPostPpapRegister
      // 
      this.panelPostPpapRegister.BackColor = System.Drawing.Color.Transparent;
      this.panelPostPpapRegister.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
      this.panelPostPpapRegister.Controls.Add(this.pictureBoxPostPpapRegisterBackButton);
      this.panelPostPpapRegister.Controls.Add(this.progressBarPostPpap);
      this.panelPostPpapRegister.Controls.Add(this.labelPostPpapStatus);
      this.panelPostPpapRegister.Controls.Add(this.buttonRegisterYourMailAddress);
      this.panelPostPpapRegister.Controls.Add(this.labelYourMailAddress);
      this.panelPostPpapRegister.Controls.Add(this.textBoxYourMailAddress);
      this.panelPostPpapRegister.Controls.Add(this.labelPpapDescription);
      this.panelPostPpapRegister.Controls.Add(this.panelHeaderPostPpapRegister);
      resources.ApplyResources(this.panelPostPpapRegister, "panelPostPpapRegister");
      this.panelPostPpapRegister.Name = "panelPostPpapRegister";
      this.panelPostPpapRegister.VisibleChanged += new System.EventHandler(this.panelPostPpapRegister_VisibleChanged);
      // 
      // pictureBoxPostPpapRegisterBackButton
      // 
      this.pictureBoxPostPpapRegisterBackButton.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxPostPpapRegisterBackButton.Cursor = System.Windows.Forms.Cursors.Hand;
      resources.ApplyResources(this.pictureBoxPostPpapRegisterBackButton, "pictureBoxPostPpapRegisterBackButton");
      this.pictureBoxPostPpapRegisterBackButton.Name = "pictureBoxPostPpapRegisterBackButton";
      this.pictureBoxPostPpapRegisterBackButton.TabStop = false;
      this.pictureBoxPostPpapRegisterBackButton.Click += new System.EventHandler(this.pictureBoxBackToMain_Click);
      // 
      // progressBarPostPpap
      // 
      resources.ApplyResources(this.progressBarPostPpap, "progressBarPostPpap");
      this.progressBarPostPpap.Name = "progressBarPostPpap";
      this.progressBarPostPpap.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
      // 
      // labelPostPpapStatus
      // 
      resources.ApplyResources(this.labelPostPpapStatus, "labelPostPpapStatus");
      this.labelPostPpapStatus.ForeColor = System.Drawing.Color.Gray;
      this.labelPostPpapStatus.Name = "labelPostPpapStatus";
      // 
      // buttonRegisterYourMailAddress
      // 
      resources.ApplyResources(this.buttonRegisterYourMailAddress, "buttonRegisterYourMailAddress");
      this.buttonRegisterYourMailAddress.Name = "buttonRegisterYourMailAddress";
      this.buttonRegisterYourMailAddress.UseVisualStyleBackColor = true;
      this.buttonRegisterYourMailAddress.Click += new System.EventHandler(this.buttonRegisterYourMailAddress_Click);
      // 
      // labelYourMailAddress
      // 
      resources.ApplyResources(this.labelYourMailAddress, "labelYourMailAddress");
      this.labelYourMailAddress.Name = "labelYourMailAddress";
      // 
      // textBoxYourMailAddress
      // 
      resources.ApplyResources(this.textBoxYourMailAddress, "textBoxYourMailAddress");
      this.textBoxYourMailAddress.Name = "textBoxYourMailAddress";
      this.textBoxYourMailAddress.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textBoxYourMailAddress_KeyDown);
      // 
      // labelPpapDescription
      // 
      resources.ApplyResources(this.labelPpapDescription, "labelPpapDescription");
      this.labelPpapDescription.Name = "labelPpapDescription";
      // 
      // panelHeaderPostPpapRegister
      // 
      this.panelHeaderPostPpapRegister.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelHeaderPostPpapRegister.Controls.Add(this.labelPostPpapPageTitle);
      this.panelHeaderPostPpapRegister.Controls.Add(this.pictureBoxPostPpapPage);
      resources.ApplyResources(this.panelHeaderPostPpapRegister, "panelHeaderPostPpapRegister");
      this.panelHeaderPostPpapRegister.Name = "panelHeaderPostPpapRegister";
      // 
      // labelPostPpapPageTitle
      // 
      resources.ApplyResources(this.labelPostPpapPageTitle, "labelPostPpapPageTitle");
      this.labelPostPpapPageTitle.BackColor = System.Drawing.Color.Transparent;
      this.labelPostPpapPageTitle.Name = "labelPostPpapPageTitle";
      // 
      // pictureBoxPostPpapPage
      // 
      resources.ApplyResources(this.pictureBoxPostPpapPage, "pictureBoxPostPpapPage");
      this.pictureBoxPostPpapPage.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxPostPpapPage.Name = "pictureBoxPostPpapPage";
      this.pictureBoxPostPpapPage.TabStop = false;
      // 
      // tabPagePostPpapMain
      // 
      this.tabPagePostPpapMain.Controls.Add(this.panelPostPpapMain);
      resources.ApplyResources(this.tabPagePostPpapMain, "tabPagePostPpapMain");
      this.tabPagePostPpapMain.Name = "tabPagePostPpapMain";
      this.tabPagePostPpapMain.UseVisualStyleBackColor = true;
      // 
      // panelPostPpapMain
      // 
      this.panelPostPpapMain.BackColor = System.Drawing.Color.Transparent;
      this.panelPostPpapMain.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
      this.panelPostPpapMain.Controls.Add(this.pictureBoxMailAddress);
      this.panelPostPpapMain.Controls.Add(this.labelRegisteredMailAddress);
      this.panelPostPpapMain.Controls.Add(this.pictureBoxInvalid);
      this.panelPostPpapMain.Controls.Add(this.pictureBoxKey);
      this.panelPostPpapMain.Controls.Add(this.pictureBoxLockIcon);
      this.panelPostPpapMain.Controls.Add(this.pictureBoxCheckIcon);
      this.panelPostPpapMain.Controls.Add(this.labelValidDetails);
      this.panelPostPpapMain.Controls.Add(this.pictureBoxValidDetails);
      this.panelPostPpapMain.Controls.Add(this.labelValidMessage);
      this.panelPostPpapMain.Controls.Add(this.pictureBoxValid);
      this.panelPostPpapMain.Controls.Add(this.buttonPuclicMailAddressSearch);
      this.panelPostPpapMain.Controls.Add(this.labelRecipientMailAddress);
      this.panelPostPpapMain.Controls.Add(this.textBoxPuclicRecipientMailAddress);
      this.panelPostPpapMain.Controls.Add(this.pictureBoxPostPpapBackButton);
      this.panelPostPpapMain.Controls.Add(this.panelHeaderPostPpapMain);
      this.panelPostPpapMain.Controls.Add(this.panelPublicDragAndDrop);
      resources.ApplyResources(this.panelPostPpapMain, "panelPostPpapMain");
      this.panelPostPpapMain.Name = "panelPostPpapMain";
      this.panelPostPpapMain.VisibleChanged += new System.EventHandler(this.panelPostPpapMain_VisibleChanged);
      // 
      // pictureBoxMailAddress
      // 
      resources.ApplyResources(this.pictureBoxMailAddress, "pictureBoxMailAddress");
      this.pictureBoxMailAddress.Name = "pictureBoxMailAddress";
      this.pictureBoxMailAddress.TabStop = false;
      // 
      // labelRegisteredMailAddress
      // 
      resources.ApplyResources(this.labelRegisteredMailAddress, "labelRegisteredMailAddress");
      this.labelRegisteredMailAddress.Name = "labelRegisteredMailAddress";
      // 
      // pictureBoxInvalid
      // 
      resources.ApplyResources(this.pictureBoxInvalid, "pictureBoxInvalid");
      this.pictureBoxInvalid.Name = "pictureBoxInvalid";
      this.pictureBoxInvalid.TabStop = false;
      // 
      // pictureBoxKey
      // 
      resources.ApplyResources(this.pictureBoxKey, "pictureBoxKey");
      this.pictureBoxKey.Name = "pictureBoxKey";
      this.pictureBoxKey.TabStop = false;
      // 
      // pictureBoxLockIcon
      // 
      resources.ApplyResources(this.pictureBoxLockIcon, "pictureBoxLockIcon");
      this.pictureBoxLockIcon.Name = "pictureBoxLockIcon";
      this.pictureBoxLockIcon.TabStop = false;
      // 
      // pictureBoxCheckIcon
      // 
      resources.ApplyResources(this.pictureBoxCheckIcon, "pictureBoxCheckIcon");
      this.pictureBoxCheckIcon.Name = "pictureBoxCheckIcon";
      this.pictureBoxCheckIcon.TabStop = false;
      // 
      // labelValidDetails
      // 
      resources.ApplyResources(this.labelValidDetails, "labelValidDetails");
      this.labelValidDetails.Name = "labelValidDetails";
      // 
      // pictureBoxValidDetails
      // 
      resources.ApplyResources(this.pictureBoxValidDetails, "pictureBoxValidDetails");
      this.pictureBoxValidDetails.Name = "pictureBoxValidDetails";
      this.pictureBoxValidDetails.TabStop = false;
      // 
      // labelValidMessage
      // 
      resources.ApplyResources(this.labelValidMessage, "labelValidMessage");
      this.labelValidMessage.Name = "labelValidMessage";
      // 
      // pictureBoxValid
      // 
      resources.ApplyResources(this.pictureBoxValid, "pictureBoxValid");
      this.pictureBoxValid.Name = "pictureBoxValid";
      this.pictureBoxValid.TabStop = false;
      // 
      // buttonPuclicMailAddressSearch
      // 
      resources.ApplyResources(this.buttonPuclicMailAddressSearch, "buttonPuclicMailAddressSearch");
      this.buttonPuclicMailAddressSearch.Name = "buttonPuclicMailAddressSearch";
      this.buttonPuclicMailAddressSearch.UseVisualStyleBackColor = true;
      this.buttonPuclicMailAddressSearch.Click += new System.EventHandler(this.buttonPuclicMailAddressSearch_Click);
      // 
      // labelRecipientMailAddress
      // 
      resources.ApplyResources(this.labelRecipientMailAddress, "labelRecipientMailAddress");
      this.labelRecipientMailAddress.Name = "labelRecipientMailAddress";
      // 
      // textBoxPuclicRecipientMailAddress
      // 
      resources.ApplyResources(this.textBoxPuclicRecipientMailAddress, "textBoxPuclicRecipientMailAddress");
      this.textBoxPuclicRecipientMailAddress.Name = "textBoxPuclicRecipientMailAddress";
      this.textBoxPuclicRecipientMailAddress.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textBoxPuclicMailAddress_KeyDown);
      // 
      // pictureBoxPostPpapBackButton
      // 
      resources.ApplyResources(this.pictureBoxPostPpapBackButton, "pictureBoxPostPpapBackButton");
      this.pictureBoxPostPpapBackButton.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxPostPpapBackButton.Cursor = System.Windows.Forms.Cursors.Hand;
      this.pictureBoxPostPpapBackButton.Name = "pictureBoxPostPpapBackButton";
      this.pictureBoxPostPpapBackButton.TabStop = false;
      this.pictureBoxPostPpapBackButton.Click += new System.EventHandler(this.pictureBoxBackToMain_Click);
      // 
      // panelHeaderPostPpapMain
      // 
      this.panelHeaderPostPpapMain.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelHeaderPostPpapMain.Controls.Add(this.labelPostPpapMain);
      this.panelHeaderPostPpapMain.Controls.Add(this.pictureBoxPostPpapMain);
      resources.ApplyResources(this.panelHeaderPostPpapMain, "panelHeaderPostPpapMain");
      this.panelHeaderPostPpapMain.Name = "panelHeaderPostPpapMain";
      // 
      // labelPostPpapMain
      // 
      resources.ApplyResources(this.labelPostPpapMain, "labelPostPpapMain");
      this.labelPostPpapMain.BackColor = System.Drawing.Color.Transparent;
      this.labelPostPpapMain.Name = "labelPostPpapMain";
      // 
      // pictureBoxPostPpapMain
      // 
      resources.ApplyResources(this.pictureBoxPostPpapMain, "pictureBoxPostPpapMain");
      this.pictureBoxPostPpapMain.BackColor = System.Drawing.Color.Transparent;
      this.pictureBoxPostPpapMain.Name = "pictureBoxPostPpapMain";
      this.pictureBoxPostPpapMain.TabStop = false;
      // 
      // panelPublicDragAndDrop
      // 
      this.panelPublicDragAndDrop.Controls.Add(this.labelPasswordSharingIfRegistered);
      resources.ApplyResources(this.panelPublicDragAndDrop, "panelPublicDragAndDrop");
      this.panelPublicDragAndDrop.Name = "panelPublicDragAndDrop";
      // 
      // labelPasswordSharingIfRegistered
      // 
      resources.ApplyResources(this.labelPasswordSharingIfRegistered, "labelPasswordSharingIfRegistered");
      this.labelPasswordSharingIfRegistered.Name = "labelPasswordSharingIfRegistered";
      // 
      // tabPageProgressState
      // 
      this.tabPageProgressState.BackColor = System.Drawing.SystemColors.Control;
      this.tabPageProgressState.Controls.Add(this.panelProgressState);
      resources.ApplyResources(this.tabPageProgressState, "tabPageProgressState");
      this.tabPageProgressState.Name = "tabPageProgressState";
      // 
      // panelProgressState
      // 
      this.panelProgressState.BackColor = System.Drawing.SystemColors.Control;
      this.panelProgressState.Controls.Add(this.pictureBoxProgressStateBackButton);
      this.panelProgressState.Controls.Add(this.panelSidebarProgress);
      this.panelProgressState.Controls.Add(this.labelCryptionType);
      this.panelProgressState.Controls.Add(this.buttonCancel);
      this.panelProgressState.Controls.Add(this.labelProgressPercentText);
      this.panelProgressState.Controls.Add(this.labelProgressMessageText);
      this.panelProgressState.Controls.Add(this.progressBar);
      resources.ApplyResources(this.panelProgressState, "panelProgressState");
      this.panelProgressState.Name = "panelProgressState";
      this.panelProgressState.VisibleChanged += new System.EventHandler(this.panelProgressState_VisibleChanged);
      this.panelProgressState.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.panelProgressState.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // pictureBoxProgressStateBackButton
      // 
      resources.ApplyResources(this.pictureBoxProgressStateBackButton, "pictureBoxProgressStateBackButton");
      this.pictureBoxProgressStateBackButton.Cursor = System.Windows.Forms.Cursors.Hand;
      this.pictureBoxProgressStateBackButton.Name = "pictureBoxProgressStateBackButton";
      this.pictureBoxProgressStateBackButton.TabStop = false;
      this.pictureBoxProgressStateBackButton.Click += new System.EventHandler(this.pictureBoxProgressStateBackButton_Click);
      this.pictureBoxProgressStateBackButton.MouseEnter += new System.EventHandler(this.pictureBoxProgressStateBackButton_MouseEnter);
      this.pictureBoxProgressStateBackButton.MouseLeave += new System.EventHandler(this.pictureBoxProgressStateBackButton_MouseLeave);
      // 
      // panelSidebarProgress
      // 
      this.panelSidebarProgress.BackColor = System.Drawing.Color.WhiteSmoke;
      this.panelSidebarProgress.Controls.Add(this.labelProgress);
      this.panelSidebarProgress.Controls.Add(this.pictureBoxProgress);
      resources.ApplyResources(this.panelSidebarProgress, "panelSidebarProgress");
      this.panelSidebarProgress.Name = "panelSidebarProgress";
      // 
      // labelProgress
      // 
      this.labelProgress.BackColor = System.Drawing.Color.Transparent;
      resources.ApplyResources(this.labelProgress, "labelProgress");
      this.labelProgress.Name = "labelProgress";
      // 
      // pictureBoxProgress
      // 
      resources.ApplyResources(this.pictureBoxProgress, "pictureBoxProgress");
      this.pictureBoxProgress.Name = "pictureBoxProgress";
      this.pictureBoxProgress.TabStop = false;
      // 
      // labelCryptionType
      // 
      resources.ApplyResources(this.labelCryptionType, "labelCryptionType");
      this.labelCryptionType.Name = "labelCryptionType";
      this.labelCryptionType.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.labelCryptionType.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // buttonCancel
      // 
      resources.ApplyResources(this.buttonCancel, "buttonCancel");
      this.buttonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
      this.buttonCancel.Name = "buttonCancel";
      this.buttonCancel.UseVisualStyleBackColor = true;
      this.buttonCancel.Click += new System.EventHandler(this.buttonCancel_Click);
      // 
      // labelProgressPercentText
      // 
      resources.ApplyResources(this.labelProgressPercentText, "labelProgressPercentText");
      this.labelProgressPercentText.Name = "labelProgressPercentText";
      this.labelProgressPercentText.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.labelProgressPercentText.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // labelProgressMessageText
      // 
      resources.ApplyResources(this.labelProgressMessageText, "labelProgressMessageText");
      this.labelProgressMessageText.Name = "labelProgressMessageText";
      this.labelProgressMessageText.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.labelProgressMessageText.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // progressBar
      // 
      resources.ApplyResources(this.progressBar, "progressBar");
      this.progressBar.Maximum = 10000;
      this.progressBar.Name = "progressBar";
      this.progressBar.Step = 1;
      this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
      this.progressBar.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.progressBar.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      // 
      // contextMenuStrip1
      // 
      this.contextMenuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
      this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.encryptToolStripMenuItem,
            this.decryptToolStripMenuItem,
            this.toolStripMenuItem3,
            this.optionToolStripMenuItem1,
            this.toolStripMenuItem4,
            this.helpToolStripMenuItem2,
            this.toolStripMenuItem5,
            this.exitToolStripMenuItem1});
      this.contextMenuStrip1.Name = "contextMenuStrip1";
      resources.ApplyResources(this.contextMenuStrip1, "contextMenuStrip1");
      // 
      // encryptToolStripMenuItem
      // 
      this.encryptToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.selectFilesToolStripMenuItem1,
            this.selectFoldersToolStripMenuItem1});
      resources.ApplyResources(this.encryptToolStripMenuItem, "encryptToolStripMenuItem");
      this.encryptToolStripMenuItem.Name = "encryptToolStripMenuItem";
      // 
      // selectFilesToolStripMenuItem1
      // 
      resources.ApplyResources(this.selectFilesToolStripMenuItem1, "selectFilesToolStripMenuItem1");
      this.selectFilesToolStripMenuItem1.Name = "selectFilesToolStripMenuItem1";
      // 
      // selectFoldersToolStripMenuItem1
      // 
      resources.ApplyResources(this.selectFoldersToolStripMenuItem1, "selectFoldersToolStripMenuItem1");
      this.selectFoldersToolStripMenuItem1.Name = "selectFoldersToolStripMenuItem1";
      // 
      // decryptToolStripMenuItem
      // 
      resources.ApplyResources(this.decryptToolStripMenuItem, "decryptToolStripMenuItem");
      this.decryptToolStripMenuItem.Name = "decryptToolStripMenuItem";
      // 
      // toolStripMenuItem3
      // 
      this.toolStripMenuItem3.Name = "toolStripMenuItem3";
      resources.ApplyResources(this.toolStripMenuItem3, "toolStripMenuItem3");
      // 
      // optionToolStripMenuItem1
      // 
      resources.ApplyResources(this.optionToolStripMenuItem1, "optionToolStripMenuItem1");
      this.optionToolStripMenuItem1.Name = "optionToolStripMenuItem1";
      // 
      // toolStripMenuItem4
      // 
      this.toolStripMenuItem4.Name = "toolStripMenuItem4";
      resources.ApplyResources(this.toolStripMenuItem4, "toolStripMenuItem4");
      // 
      // helpToolStripMenuItem2
      // 
      resources.ApplyResources(this.helpToolStripMenuItem2, "helpToolStripMenuItem2");
      this.helpToolStripMenuItem2.Name = "helpToolStripMenuItem2";
      // 
      // toolStripMenuItem5
      // 
      this.toolStripMenuItem5.Name = "toolStripMenuItem5";
      resources.ApplyResources(this.toolStripMenuItem5, "toolStripMenuItem5");
      // 
      // exitToolStripMenuItem1
      // 
      resources.ApplyResources(this.exitToolStripMenuItem1, "exitToolStripMenuItem1");
      this.exitToolStripMenuItem1.Name = "exitToolStripMenuItem1";
      // 
      // contextMenuStrip2
      // 
      this.contextMenuStrip2.ImageScalingSize = new System.Drawing.Size(20, 20);
      this.contextMenuStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.ToolStripMenuItemAtcFile,
            this.ToolStripMenuItemExeFile,
            this.ToolStripMenuItemRsa});
      this.contextMenuStrip2.Name = "contextMenuStrip2";
      resources.ApplyResources(this.contextMenuStrip2, "contextMenuStrip2");
      // 
      // ToolStripMenuItemAtcFile
      // 
      resources.ApplyResources(this.ToolStripMenuItemAtcFile, "ToolStripMenuItemAtcFile");
      this.ToolStripMenuItemAtcFile.Name = "ToolStripMenuItemAtcFile";
      this.ToolStripMenuItemAtcFile.Click += new System.EventHandler(this.ToolStripMenuItemAtcFile_Click);
      // 
      // ToolStripMenuItemExeFile
      // 
      resources.ApplyResources(this.ToolStripMenuItemExeFile, "ToolStripMenuItemExeFile");
      this.ToolStripMenuItemExeFile.Name = "ToolStripMenuItemExeFile";
      this.ToolStripMenuItemExeFile.Click += new System.EventHandler(this.ToolStripMenuItemExeFile_Click);
      // 
      // ToolStripMenuItemRsa
      // 
      resources.ApplyResources(this.ToolStripMenuItemRsa, "ToolStripMenuItemRsa");
      this.ToolStripMenuItemRsa.Name = "ToolStripMenuItemRsa";
      this.ToolStripMenuItemRsa.Click += new System.EventHandler(this.ToolStripMenuItemRsa_Click);
      // 
      // notifyIcon1
      // 
      this.notifyIcon1.ContextMenuStrip = this.contextMenuStrip1;
      resources.ApplyResources(this.notifyIcon1, "notifyIcon1");
      this.notifyIcon1.MouseClick += new System.Windows.Forms.MouseEventHandler(this.notifyIcon1_MouseClick);
      // 
      // saveFileDialog1
      // 
      this.saveFileDialog1.DefaultExt = "atc";
      resources.ApplyResources(this.saveFileDialog1, "saveFileDialog1");
      // 
      // toolTipZxcvbnWarning
      // 
      this.toolTipZxcvbnWarning.IsBalloon = true;
      this.toolTipZxcvbnWarning.ToolTipIcon = System.Windows.Forms.ToolTipIcon.Warning;
      // 
      // Form1
      // 
      this.AllowDrop = true;
      resources.ApplyResources(this, "$this");
      this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
      this.Controls.Add(this.panelOuter);
      this.Controls.Add(this.statusStrip1);
      this.Controls.Add(this.menuStrip1);
      this.DoubleBuffered = true;
      this.KeyPreview = true;
      this.MainMenuStrip = this.menuStrip1;
      this.Name = "Form1";
      this.Activated += new System.EventHandler(this.Form1_Activated);
      this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.Form1_FormClosed);
      this.Load += new System.EventHandler(this.Form1_Load);
      this.Shown += new System.EventHandler(this.Form1_Shown);
      this.DragDrop += new System.Windows.Forms.DragEventHandler(this.Form1_DragDrop);
      this.DragEnter += new System.Windows.Forms.DragEventHandler(this.Form1_DragEnter);
      this.DragLeave += new System.EventHandler(this.Form1_DragLeave);
      this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.Form1_KeyDown);
      this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseDown);
      this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.Form1_MouseMove);
      this.Resize += new System.EventHandler(this.Form1_Resize);
      this.statusStrip1.ResumeLayout(false);
      this.statusStrip1.PerformLayout();
      this.menuStrip1.ResumeLayout(false);
      this.menuStrip1.PerformLayout();
      this.panelOuter.ResumeLayout(false);
      this.tabControl1.ResumeLayout(false);
      this.tabPageStartPage.ResumeLayout(false);
      this.panelStartPage.ResumeLayout(false);
      this.panelStartPage.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxOption)).EndInit();
      this.panelSidebarStart.ResumeLayout(false);
      this.panelSidebarStart.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpap)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxDec)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsa)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxAtc)).EndInit();
      this.tabPageEncrypt.ResumeLayout(false);
      this.panelEncrypt.ResumeLayout(false);
      this.panelEncrypt.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPassStrengthMeter)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrengthEmpty)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength04)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength03)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength02)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength01)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPasswordStrength00)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxEncryptBackToMain)).EndInit();
      this.panelSidebarEncrypt.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxEncryption)).EndInit();
      this.tabPageEncryptConfirm.ResumeLayout(false);
      this.panelEncryptConfirm.ResumeLayout(false);
      this.panelEncryptConfirm.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPassStrengthMeterConfirm)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxEncryptConfirmBackButton)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCheckPasswordValidation)).EndInit();
      this.panelSidebarEncryptConfirm.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxEncryptionConfirm)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxInValidIcon)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxValidIcon)).EndInit();
      this.tabPageDecrypt.ResumeLayout(false);
      this.panelDecrypt.ResumeLayout(false);
      this.panelDecrypt.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxDecryptBackButton)).EndInit();
      this.panelSidebarDecrypt.ResumeLayout(false);
      this.panelSidebarDecrypt.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxDecryption)).EndInit();
      this.tabPageRsa.ResumeLayout(false);
      this.panelRsa.ResumeLayout(false);
      this.panelRsa.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsaBackButton)).EndInit();
      this.panelHeaderRsa.ResumeLayout(false);
      this.panelHeaderRsa.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsaPage)).EndInit();
      this.tabPageRsaKey.ResumeLayout(false);
      this.panelRsaKey.ResumeLayout(false);
      this.panelRsaKey.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxInfoBalloon)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPublicAndPrivateKey)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPrivateKey)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPublicKey)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsaKeyBackButton)).EndInit();
      this.panelHeaderRsaKey.ResumeLayout(false);
      this.panelHeaderRsaKey.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxRsaType)).EndInit();
      this.tabPagePostPpapRegister.ResumeLayout(false);
      this.panelPostPpapRegister.ResumeLayout(false);
      this.panelPostPpapRegister.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpapRegisterBackButton)).EndInit();
      this.panelHeaderPostPpapRegister.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpapPage)).EndInit();
      this.tabPagePostPpapMain.ResumeLayout(false);
      this.panelPostPpapMain.ResumeLayout(false);
      this.panelPostPpapMain.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxMailAddress)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxInvalid)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxKey)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxLockIcon)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCheckIcon)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxValidDetails)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxValid)).EndInit();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpapBackButton)).EndInit();
      this.panelHeaderPostPpapMain.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxPostPpapMain)).EndInit();
      this.panelPublicDragAndDrop.ResumeLayout(false);
      this.tabPageProgressState.ResumeLayout(false);
      this.panelProgressState.ResumeLayout(false);
      this.panelProgressState.PerformLayout();
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxProgressStateBackButton)).EndInit();
      this.panelSidebarProgress.ResumeLayout(false);
      ((System.ComponentModel.ISupportInitialize)(this.pictureBoxProgress)).EndInit();
      this.contextMenuStrip1.ResumeLayout(false);
      this.contextMenuStrip2.ResumeLayout(false);
      this.ResumeLayout(false);
      this.PerformLayout();

    }

    #endregion

    private System.Windows.Forms.StatusStrip statusStrip1;
    private System.Windows.Forms.MenuStrip menuStrip1;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemFile;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemEncrypt;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemOption;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemSetting;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemHelp;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemHelpContents;
    private System.Windows.Forms.ToolStripSeparator toolStripMenuItem1;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemAbout;
    private System.Windows.Forms.ToolStripSeparator toolStripMenuItem2;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemExit;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemEncryptSelectFiles;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemEncryptSelectFolder;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemDecrypt;
    private System.Windows.Forms.Panel panelOuter;
    private System.Windows.Forms.TabControl tabControl1;
    private System.Windows.Forms.TabPage tabPageStartPage;
    private System.Windows.Forms.TabPage tabPageEncrypt;
    private System.Windows.Forms.Panel panelEncrypt;
    private System.Windows.Forms.TabPage tabPageDecrypt;
    private System.Windows.Forms.Panel panelDecrypt;
    private System.Windows.Forms.TabPage tabPageProgressState;
    private System.Windows.Forms.Panel panelProgressState;
    private System.Windows.Forms.Button buttonCancel;
    private System.Windows.Forms.Label labelProgressPercentText;
    private System.Windows.Forms.Label labelProgressMessageText;
    private System.Windows.Forms.ProgressBar progressBar;
    private System.Windows.Forms.Label labelPassword;
    private System.Windows.Forms.Button buttonEncryptCancel;
    private System.Windows.Forms.Button buttonDecryptCancel;
    private System.Windows.Forms.Button buttonDecryptStart;
    private System.Windows.Forms.Label labelDecryptionPassword;
    private System.Windows.Forms.NotifyIcon notifyIcon1;
    private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
    private System.Windows.Forms.ToolStripMenuItem encryptToolStripMenuItem;
    private System.Windows.Forms.ToolStripMenuItem selectFilesToolStripMenuItem1;
    private System.Windows.Forms.ToolStripMenuItem selectFoldersToolStripMenuItem1;
    private System.Windows.Forms.ToolStripMenuItem decryptToolStripMenuItem;
    private System.Windows.Forms.ToolStripSeparator toolStripMenuItem3;
    private System.Windows.Forms.ToolStripMenuItem optionToolStripMenuItem1;
    private System.Windows.Forms.ToolStripSeparator toolStripMenuItem4;
    private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem2;
    private System.Windows.Forms.ToolStripSeparator toolStripMenuItem5;
    private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem1;
    private System.Windows.Forms.SaveFileDialog saveFileDialog1;
    private System.Windows.Forms.OpenFileDialog openFileDialog1;
    private System.Windows.Forms.CheckBox checkBoxDeleteAtcFileAfterDecryption;
    private System.Windows.Forms.ContextMenuStrip contextMenuStrip2;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemAtcFile;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemRsa;
    private System.Windows.Forms.ToolStripMenuItem ToolStripMenuItemExeFile;
    private System.ComponentModel.BackgroundWorker backgroundWorker1;
    private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
    private System.Windows.Forms.Label labelCryptionType;
    private System.Windows.Forms.TabPage tabPageEncryptConfirm;
    private System.Windows.Forms.Panel panelSidebarEncrypt;
    internal System.Windows.Forms.PictureBox pictureBoxEncryption;
    private System.Windows.Forms.Label labelEncryption;
    private System.Windows.Forms.Button buttonEncryptionPasswordOk;
    private System.Windows.Forms.Panel panelEncryptConfirm;
    private System.Windows.Forms.Panel panelSidebarEncryptConfirm;
    private System.Windows.Forms.Label labelEncryptionConfirm;
    internal System.Windows.Forms.PictureBox pictureBoxEncryptionConfirm;
    private System.Windows.Forms.Button buttonEncryptionConfirmCancel;
    private System.Windows.Forms.Button buttonEncryptStart;
    private System.Windows.Forms.PictureBox pictureBoxInValidIcon;
    private System.Windows.Forms.PictureBox pictureBoxValidIcon;
    private System.Windows.Forms.CheckBox checkBoxReDeleteOriginalFileAfterEncryption;
    private System.Windows.Forms.Label labelInputPasswordAgain;
    private System.Windows.Forms.Panel panelSidebarDecrypt;
    internal System.Windows.Forms.PictureBox pictureBoxDecryption;
    private System.Windows.Forms.Label labelDecryption;
    private System.Windows.Forms.PictureBox pictureBoxCheckPasswordValidation;
    private System.Windows.Forms.Panel panelSidebarProgress;
    private System.Windows.Forms.Label labelProgress;
    internal System.Windows.Forms.PictureBox pictureBoxProgress;
    private System.Windows.Forms.Panel panelStartPage;
    private System.Windows.Forms.Panel panelSidebarStart;
    private System.Windows.Forms.Label labelDragAndDrop;
    private System.Windows.Forms.Button buttonExit;
    private System.Windows.Forms.PictureBox pictureBoxEncryptBackToMain;
    private System.Windows.Forms.PictureBox pictureBoxEncryptConfirmBackButton;
    private System.Windows.Forms.PictureBox pictureBoxDecryptBackButton;
    private System.Windows.Forms.PictureBox pictureBoxProgressStateBackButton;
    private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabelDataVersion;
    private System.Windows.Forms.PictureBox pictureBoxPasswordStrengthEmpty;
    private System.Windows.Forms.PictureBox pictureBoxPasswordStrength04;
    private System.Windows.Forms.PictureBox pictureBoxPasswordStrength03;
    private System.Windows.Forms.PictureBox pictureBoxPasswordStrength02;
    private System.Windows.Forms.PictureBox pictureBoxPasswordStrength01;
    private System.Windows.Forms.PictureBox pictureBoxPasswordStrength00;
    private System.Windows.Forms.PictureBox pictureBoxPassStrengthMeter;
    private System.Windows.Forms.CheckBox checkBoxDeleteOriginalFileAfterEncryption;
    private System.Windows.Forms.Label labelPasswordStrength;
    private System.Windows.Forms.ToolTip toolTipZxcvbnWarning;
    private System.Windows.Forms.ToolTip toolTipZxcvbnSuggestions;
    private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabelEncryptionTime;
    private System.Windows.Forms.Label labelDec;
    protected internal System.Windows.Forms.PictureBox pictureBoxDec;
    private System.Windows.Forms.Label labelRsaDetail;
    internal System.Windows.Forms.PictureBox pictureBoxRsa;
    internal System.Windows.Forms.PictureBox pictureBoxPostPpap;
    private System.Windows.Forms.Label labelAtc;
    internal System.Windows.Forms.PictureBox pictureBoxAtc;
    private System.Windows.Forms.Label labelPostPpap;
    private System.Windows.Forms.TabPage tabPageRsa;
    private System.Windows.Forms.Panel panelRsa;
    private System.Windows.Forms.Label labelRsaMessage;
    private System.Windows.Forms.Button buttonRsaCancel;
    private System.Windows.Forms.Button buttonGenerateKey;
    private System.Windows.Forms.Panel panelHeaderRsa;
    private System.Windows.Forms.Label labelRsa;
    internal System.Windows.Forms.PictureBox pictureBoxRsaPage;
    private System.Windows.Forms.SaveFileDialog saveFileDialog2;
    private System.Windows.Forms.PictureBox pictureBoxRsaBackButton;
    private System.Windows.Forms.TabPage tabPageRsaKey;
    private System.Windows.Forms.Panel panelRsaKey;
    private System.Windows.Forms.PictureBox pictureBoxRsaKeyBackButton;
    private System.Windows.Forms.Button buttonRsaKeyCancel;
    private System.Windows.Forms.Panel panelHeaderRsaKey;
    private System.Windows.Forms.Label labelRsaKeyName;
    internal System.Windows.Forms.PictureBox pictureBoxRsaType;
    internal System.Windows.Forms.PictureBox pictureBoxPublicAndPrivateKey;
    private System.Windows.Forms.PictureBox pictureBoxPrivateKey;
    private System.Windows.Forms.PictureBox pictureBoxPublicKey;
    private System.Windows.Forms.Label labelRsaDescription;
    private System.Windows.Forms.TextBox textBoxHashString;
    private System.Windows.Forms.ComboBox comboBoxHashList;
    private System.Windows.Forms.Label labelRsaInformation;
    private System.Windows.Forms.PictureBox pictureBoxInfoBalloon;
    internal PictureBox pictureBoxOption;
    private TabPage tabPagePostPpapRegister;
    private System.Windows.Forms.Panel panelPostPpapRegister;
    private System.Windows.Forms.Panel panelHeaderPostPpapRegister;
    private PictureBox pictureBoxPostPpapPage;
    private Label labelPostPpapPageTitle;
    internal PictureBox pictureBoxPostPpapRegisterBackButton;
    private Button buttonRegisterYourMailAddress;
    private Label labelYourMailAddress;
    private TextBox textBoxYourMailAddress;
    private Label labelPpapDescription;
    private Label labelPostPpapStatus;
    private System.Windows.Forms.ProgressBar progressBarPostPpap;
    private TabPage tabPagePostPpapMain;
    private System.Windows.Forms.Panel panelPostPpapMain;
    internal PictureBox pictureBoxPostPpapBackButton;
    private Panel panelHeaderPostPpapMain;
    private Label labelPostPpapMain;
    private PictureBox pictureBoxPostPpapMain;
    private Button buttonPuclicMailAddressSearch;
    private Label labelRecipientMailAddress;
    private TextBox textBoxPuclicRecipientMailAddress;
    private PictureBox pictureBoxValidDetails;
    private Label labelValidMessage;
    private PictureBox pictureBoxValid;
    private Panel panelPublicDragAndDrop;
    private Label labelValidDetails;
    private PictureBox pictureBoxInvalid;
    private PictureBox pictureBoxKey;
    private PictureBox pictureBoxLockIcon;
    private PictureBox pictureBoxCheckIcon;
    private Label labelRegisteredMailAddress;
    private PictureBox pictureBoxMailAddress;
    private EyeDelayPasswordTextBox textBoxPassword;
    private EyeDelayPasswordTextBox textBoxRePassword;
    private EyeDelayPasswordTextBox textBoxDecryptPassword;
    private PictureBox pictureBoxPassStrengthMeterConfirm;
    private Label labelPasswordStrengthConfirm;
    public ToolStripStatusLabel toolStripStatusLabelLicense;
    private Label labelPasswordSharingIfRegistered;
    private RoundedBorderLabel roundedBorderLabelSalvageMode;
  }
}

