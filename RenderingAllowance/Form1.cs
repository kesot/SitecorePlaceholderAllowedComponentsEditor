using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace RenderingAllowance
{
    public partial class Form1 : Form
    {
        private string scProjPath;
        private List<TreeNode> selectedNodes;
        string ProjectDirectory => Path.GetDirectoryName(scProjPath);

        public Form1()
        {
            InitializeComponent();
            openFileDialog1.Filter = "Sitecore project|*.scproj";
            openFileDialog1.FileOk += (sender, args) =>
            {
                treeView1.Nodes.Clear();
                treeView1.SelectedNodes.Clear();
                scProjPath = openFileDialog1.FileName;
                FindAllPlaceholders();
                treeView1.ExpandAll();
            };
        }

        private void selectScprojToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog1.ShowDialog();
        }

        private void FindAllPlaceholders()
        {
            if (scProjPath == null)
                throw new InvalidOperationException($"{nameof(scProjPath)} == null");
            var doc = new XmlDocument();
            doc.Load(scProjPath);
            var sitecoreXElements = doc.GetElementsByTagName("SitecoreItem");
            for (var i = 0; i < sitecoreXElements.Count; i++)
            {
                XmlNode sitecoreXElement = sitecoreXElements[i];
                var itemPath = HttpUtility.UrlDecode(sitecoreXElement.Attributes.GetNamedItem("Include").Value);
                var itemFileText = File.ReadAllText(Path.Combine(ProjectDirectory, itemPath));
                if (itemFileText.Contains("templatekey: Placeholder"))
                {
                    CreateNodeAndParents(itemPath);
                }
            }
        }

        private void CreateNodeAndParents(string itemPath)
        {
            var itemsPaths = itemPath.Split('\\');
            var rootNode = treeView1.Nodes[itemsPaths[0]] ?? treeView1.Nodes.Add(itemsPaths[0], itemsPaths[0]);
            var pointer = rootNode;
            foreach (var key in itemsPaths.Skip(1).Take(itemsPaths.Length))
            {
                var node = pointer.Nodes[key] ?? pointer.Nodes.Add(key, key);
                pointer = node;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (scProjPath == null)
            {
                MessageBox.Show("Select sitecore project");
                return;
            }
            var guidString = textBox1.Text;
            Guid guid;
            Guid.TryParse(guidString.Replace("{", "").Replace("}", ""), out guid);
            if (guid == Guid.Empty)
            {
                MessageBox.Show("incorrect guid");
                return;
            }
            UpdateAllowanceForSelected(guid);
        }

        private void UpdateAllowanceForSelected(Guid guid)
        {
            var filesToSave = new Dictionary<string, IEnumerable<string>>();

            foreach (var selectedNode in treeView1.SelectedNodes)
            {
                var nodePath = selectedNode.FullPath;
                var nodeFullPath = Path.Combine(ProjectDirectory, nodePath);
                var itemFileLines = File.ReadAllLines(nodeFullPath);
                var allowanceKeyIndex = GetFieldIndex(nodeFullPath, "key: allowed controls", itemFileLines);

                var contentLengthIndex = allowanceKeyIndex + 1;
                var contentIndex = contentLengthIndex + 2;

                // todo order by Name
                var allowedControlGuids =
                    new HashSet<Guid>(itemFileLines[contentIndex].Replace("{", "").Replace("}", "")
                        .Split(new [] {'|'}, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => Guid.Parse(s)));

                allowedControlGuids.Add(guid);

                itemFileLines[contentIndex] = string.Join("|",
                    allowedControlGuids.Select(g => g.ToString("B").ToUpper()));

                itemFileLines[contentLengthIndex] = $"content-length: {itemFileLines[contentIndex].Length}";
                filesToSave.Add(nodeFullPath, itemFileLines);

                var updatedAtIndex = GetFieldIndex(nodeFullPath, "key: __updated", itemFileLines, 3);
                var updatedAtString = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
                itemFileLines[updatedAtIndex] = updatedAtString;

                var newRevisionGuid = Guid.NewGuid().ToString("D");
                var revisionIndex = GetFieldIndex(nodeFullPath, "key: __revision", itemFileLines, 3);
                itemFileLines[revisionIndex] = newRevisionGuid;

                var versionIndex = GetFieldIndex(nodeFullPath, "----version----", itemFileLines, 3);
                itemFileLines[versionIndex] = $"revision: {newRevisionGuid}";
            }

            foreach (var fileToSave in filesToSave)
            {
                File.WriteAllLines(fileToSave.Key, fileToSave.Value);
            }
            toolStripStatusLabel1.Text = $"Done. {DateTime.Now:G}";
        }

        private int GetFieldIndex(string nodeFullPath, string fieldName, string[] lines, int offset = 0)
        {
            var index = lines
                    .Select((l, i) => new { Line = l, Index = i })
                    .FirstOrDefault(l => l.Line == fieldName)
                    ?.Index + offset;

            if (!index.HasValue)
            {
                throw new InvalidOperationException($"Placeholder doesn't have field: ${fieldName}. No changes saved! Path: {nodeFullPath}");
            }
            return index.Value;
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            toolStripStatusLabel1.Text = "";
        }
    }
}
