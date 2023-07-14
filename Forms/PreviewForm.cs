﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Reflection;
using System.Timers;
using System.Windows.Forms;
using OpenTK;
using PSXPrev.Classes;
using Color = System.Drawing.Color;
using Timer = System.Timers.Timer;

namespace PSXPrev
{
    public partial class PreviewForm : Form
    {
        private const float MouseSensivity = 0.0035f;

        private static readonly Pen Black3Px = new Pen(Color.Black, 3f);
        private static readonly Pen White1Px = new Pen(Color.White, 1f);
        private readonly List<Animation> _animations;
        private readonly Action<PreviewForm> _refreshAction;
        private readonly List<RootEntity> _rootEntities;
        private readonly List<Texture> _textures;

        private Timer _animateTimer;
        private Animation _curAnimation;
        private float _curAnimationFrame;
        private AnimationFrame _curAnimationFrameObj;
        private AnimationObject _curAnimationObject;
        private float _curAnimationTime;
        private Scene.GizmoId _hoveredGizmo;
        private bool _inAnimationTab;
        private float _lastMouseX;
        private float _lastMouseY;
        private bool _shiftKeyDown;
        private bool _controlKeyDown;
        private GLControl _openTkControl;
        private Vector3 _pickedPosition;
        private bool _playing;
        private Timer _redrawTimer;
        private readonly Scene _scene;
        private Scene.GizmoId _selectedGizmo;
        private Tuple<ModelEntity, Triangle> _selectedTriangle;
        private ModelEntity _selectedModelEntity;
        private RootEntity _selectedRootEntity;
        private EntitySelectionSource _selectionSource;
        private bool _showUv = true;
        private readonly VRAMPages _vram;
        private Bitmap _maskColorBitmap;
        private Bitmap _ambientColorBitmap;
        private Bitmap _backgroundColorBitmap;
        private float _texturePreviewScale = 1f;

        public PreviewForm(Action<PreviewForm> refreshAction)
        {
            _refreshAction = refreshAction;
            _animations = new List<Animation>();
            _textures = new List<Texture>();
            _rootEntities = new List<RootEntity>();
            _scene = new Scene();
            _vram = new VRAMPages(_scene);
            refreshAction(this);
            Toolkit.Init();
            InitializeComponent();
            SetupControls();
        }

        private bool Playing
        {
            get => _playing;
            set
            {
                _playing = value;
                if (_playing)
                {
                    animationPlayButton.Text = "Stop Animation";
                    _animateTimer.Start();
                }
                else
                {
                    animationPlayButton.Text = "Play Animation";
                    _animateTimer.Stop();
                }
            }
        }

        private void EntityAdded(RootEntity entity)
        {
            foreach (var entityBase in entity.ChildEntities)
            {
                var model = (ModelEntity)entityBase;
                model.TexturePage = VRAMPages.ClampTexturePage(model.TexturePage);
                model.Texture = _vram[model.TexturePage];
            }
            entitiesTreeView.BeginUpdate();
            var entityNode = entitiesTreeView.Nodes.Add(entity.EntityName);
            entityNode.Tag = entity;
            for (var m = 0; m < entity.ChildEntities.Length; m++)
            {
                var entityChildEntity = entity.ChildEntities[m];
                var modelNode = new TreeNode(entityChildEntity.EntityName);
                modelNode.Tag = entityChildEntity;
                entityNode.Nodes.Add(modelNode);
                modelNode.HideCheckBox();
                modelNode.HideCheckBox();
            }
            entitiesTreeView.EndUpdate();
        }

        private void TextureAdded(Texture texture, int index)
        {
            thumbsImageList.Images.Add(texture.Bitmap);
            texturesListView.Items.Add(texture.TextureName, index);
        }

        private void AnimationAdded(Animation animation)
        {
            animationsTreeView.BeginUpdate();
            var animationNode = new TreeNode(animation.AnimationName);
            animationNode.Tag = animation;
            animationsTreeView.Nodes.Add(animationNode);
            AddAnimationObject(animation.RootAnimationObject, animationNode);
            animationsTreeView.EndUpdate();
        }

        public void UpdateRootEntities(List<RootEntity> entities)
        {
            foreach (var entity in entities)
            {
                if (_rootEntities.Contains(entity))
                {
                    continue;
                }
                _rootEntities.Add(entity);
                EntityAdded(entity);
            }
        }

        public void UpdateTextures(List<Texture> textures)
        {
            foreach (var texture in textures)
            {
                if (_textures.Contains(texture))
                {
                    continue;
                }
                _textures.Add(texture);
                var textureIndex = _textures.IndexOf(texture);
                TextureAdded(texture, textureIndex);
            }
        }

        public void UpdateAnimations(List<Animation> animations)
        {
            foreach (var animation in animations)
            {
                if (_animations.Contains(animation))
                {
                    continue;
                }
                _animations.Add(animation);
                AnimationAdded(animation);
            }
        }

        public void SetAutoAttachLimbs(bool attachLimbs)
        {
            if (InvokeRequired)
            {
                var invokeAction = new Action<bool>(SetAutoAttachLimbs);
                Invoke(invokeAction, attachLimbs);
            }
            else
            {
                autoAttachLimbsToolStripMenuItem.Checked = attachLimbs;
                _scene.AutoAttach = attachLimbs;
                UpdateSelectedEntity();
            }
        }

        public void SelectFirstEntity()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(SelectFirstEntity));
            }
            else
            {
                // Don't select the first model if we already have a selection.
                // Doing that would interrupt the user.
                if (entitiesTreeView.SelectedNode == null && _rootEntities.Count > 0)
                {
                    SelectEntity(_rootEntities[0], true); // Select and focus
                }
            }
        }

        public void DrawAllTexturesToVRAM()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(DrawAllTexturesToVRAM));
            }
            else
            {
                foreach (var texture in _textures)
                {
                    _vram.DrawTexture(texture, true); // Suppress updates to scene until all textures are drawn.
                }
                _vram.UpdateAllPages();
            }
        }

        private void SetupControls()
        {
            _openTkControl = new GLControl { BackColor = Color.Black, Name = "openTKControl", TabIndex = 15, VSync = true };
            _openTkControl.Load += openTKControl_Load;
            _openTkControl.MouseDown += delegate (object sender, MouseEventArgs e) { openTkControl_MouseEvent(e, MouseEventType.Down); };
            _openTkControl.MouseUp += delegate (object sender, MouseEventArgs e) { openTkControl_MouseEvent(e, MouseEventType.Up); };
            _openTkControl.MouseWheel += delegate (object sender, MouseEventArgs e) { openTkControl_MouseEvent(e, MouseEventType.Wheel); };
            _openTkControl.MouseMove += delegate (object sender, MouseEventArgs e) { openTkControl_MouseEvent(e, MouseEventType.Move); };
            _openTkControl.Paint += _openTkControl_Paint;
            _openTkControl.Resize += _openTkControl_Resize;
            _openTkControl.Dock = DockStyle.Fill;
            _openTkControl.Parent = modelsSplitContainer.Panel2;
            texturePanel.MouseWheel += TexturePanelOnMouseWheel;
            UpdateLightDirection();
            ResizeToolStrip();
        }

        private void TexturePanelOnMouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0)
            {
                _texturePreviewScale *= 2f;
            }
            else
            {
                _texturePreviewScale /= 2f;
            }
            _texturePreviewScale = Math.Max(0.25f, Math.Min(8.0f, _texturePreviewScale));
            var texture = GetSelectedTexture();
            if (texture == null)
            {
                return;
            }
            texturePreviewPictureBox.Width = (int)(texture.Width * _texturePreviewScale);
            texturePreviewPictureBox.Height = (int)(texture.Height * _texturePreviewScale);
            zoomLabel.Text = string.Format("{0:P0}", _texturePreviewScale);
        }

        private void _openTkControl_Resize(object sender, EventArgs e)
        {
            _openTkControl.MakeCurrent();
            if (_scene.Initialized)
            {
                _scene.Resize(_openTkControl.Size.Width, _openTkControl.Size.Height);
            }
            else
            {
                _scene.Initialize(_openTkControl.Size.Width, _openTkControl.Size.Height);
            }
        }

        private void _openTkControl_Paint(object sender, PaintEventArgs e)
        {
            _openTkControl.MakeCurrent();
            if (_inAnimationTab && _curAnimation != null)
            {
                var checkedEntities = GetCheckedEntities();
                if (!_scene.AnimationBatch.SetupAnimationFrame(_curAnimationFrame, checkedEntities, _selectedRootEntity, _selectedModelEntity, true))
                {
                    _curAnimationFrame = 0f;
                    _curAnimationTime = 0f;
                }
                else
                {
                    // Update attached limbs while animating.
                    (_selectedRootEntity ?? _selectedModelEntity?.GetRootEntity())?.FixConnections();
                }
            }
            _scene.Draw();
            _openTkControl.SwapBuffers();
        }

        private void SetupScene()
        {
            _scene.Initialize(Width, Height);
        }

        private void SetupColors()
        {
            SetMaskColor(Color.Black);
            SetAmbientColor(Color.LightGray);
            SetBackgroundColor(Color.LightSkyBlue);
        }

        private void SetupTextures()
        {
            for (var index = 0; index < _textures.Count; index++)
            {
                var texture = _textures[index];
                TextureAdded(texture, index);
            }
        }

        private void SetupEntities()
        {
            foreach (var entity in _rootEntities)
            {
                EntityAdded(entity);
            }
        }

        private void SetupAnimations()
        {
            foreach (var animation in _animations)
            {
                AnimationAdded(animation);
            }
        }

        private void AddAnimationObject(AnimationObject parent, TreeNode parentNode)
        {
            var animationObjects = parent.Children;
            for (var o = 0; o < animationObjects.Count; o++)
            {
                var animationObject = animationObjects[o];
                var animationObjectNode = new TreeNode("Animation-Object " + (o + 1));
                animationObjectNode.Tag = animationObject;
                parentNode.Nodes.Add(animationObjectNode);
                AddAnimationObject(animationObject, animationObjectNode);
            }
        }

        private void SetupVRAM()
        {
            _vram.Setup();
        }

        private void previewForm_Load(object sender, EventArgs e)
        {
            _redrawTimer = new Timer();
            _redrawTimer.Interval = 1f / 60f;
            _redrawTimer.Elapsed += _redrawTimer_Elapsed;
            _redrawTimer.SynchronizingObject = this;
            _redrawTimer.Start();
            _animateTimer = new Timer();
            _animateTimer.Elapsed += _animateTimer_Elapsed;
            _animateTimer.SynchronizingObject = this;
            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            Text = $@"{Text} {fileVersionInfo.FileVersion}";
        }

        private void _animateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            _curAnimationTime += (float)_animateTimer.Interval;
            _curAnimationFrame = _curAnimationTime / (1f / _curAnimation.FPS);
            if (_curAnimationFrame > _curAnimation.FrameCount - 1 + 0.9999f)
            {
                _curAnimationTime = 0f;
                _curAnimationFrame = 0f;
            }
            UpdateFrameLabel();
        }

        private void UpdateFrameLabel()
        {
            animationFrameLabel.Text = $"{(int)_curAnimationFrame}/{(int)_curAnimation.FrameCount}";
            animationFrameLabel.Refresh();
        }

        private void _redrawTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Redraw();
        }

        private RootEntity[] GetCheckedEntities()
        {
            var selectedEntities = new List<RootEntity>();
            for (var i = 0; i < entitiesTreeView.Nodes.Count; i++)
            {
                var node = entitiesTreeView.Nodes[i];
                if (node.Checked)
                {
                    selectedEntities.Add(_rootEntities[i]);
                }
            }
            return selectedEntities.Count == 0 ? null : selectedEntities.ToArray();
        }

        private DialogResult ShowEntityFolderSelect(out string path)
        {
            var fbd = new FolderBrowserDialog { Description = "Select the output folder" };
            var result = fbd.ShowDialog();
            path = fbd.SelectedPath;
            return result;
        }

        private void exportEntityButton_Click(object sender, EventArgs e)
        {
            cmsModelExport.Show(exportEntityButton, exportEntityButton.Width, 0);
        }

        private void exportBitmapButton_Click(object sender, EventArgs e)
        {
            var selectedIndices = texturesListView.SelectedIndices;
            var selectedCount = selectedIndices.Count;
            if (selectedCount == 0)
            {
                MessageBox.Show("Select the textures to export first");
                return;
            }
            var fbd = new FolderBrowserDialog { Description = "Select the output folder" };
            if (fbd.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            var selectedTextures = new Texture[selectedCount];
            for (var i = 0; i < selectedCount; i++)
            {
                selectedTextures[i] = _textures[selectedIndices[i]];
            }
            var exporter = new PngExporter();
            exporter.Export(selectedTextures, fbd.SelectedPath);
            MessageBox.Show("Textures exported");
        }

        private void SelectEntity(EntityBase entity, bool focus = false)
        {
            if (!focus)
            {
                _selectionSource = EntitySelectionSource.Click;
            }
            TreeNode newNode = null;
            if (entity is RootEntity rootEntity)
            {
                var rootIndex = _rootEntities.IndexOf(rootEntity);
                newNode = entitiesTreeView.Nodes[rootIndex];
            }
            else if (entity != null)
            {
                if (entity.ParentEntity is RootEntity rootEntityFromSub)
                {
                    var rootIndex = _rootEntities.IndexOf(rootEntityFromSub);
                    var rootNode = entitiesTreeView.Nodes[rootIndex];
                    var subIndex = Array.IndexOf(rootEntityFromSub.ChildEntities, entity);
                    newNode = rootNode.Nodes[subIndex];
                }
            }
            if (newNode != null && newNode == entitiesTreeView.SelectedNode)
            {
                // entitiesTreeView_AfterSelect won't be called. Reset the selection source.
                _selectionSource = EntitySelectionSource.None;
                if (entity != null)
                {
                    UnselectTriangle();
                }
            }
            else
            {
                entitiesTreeView.SelectedNode = newNode;
            }
        }

        private void SelectTriangle(Tuple<ModelEntity, Triangle> triangle)
        {
            if (_selectedTriangle?.Item2 != triangle?.Item2)
            {
                _selectedTriangle = triangle;
                UpdateSelectedTriangle();
                UpdateModelPropertyGrid();
            }
        }

        private void UnselectTriangle()
        {
            SelectTriangle(null);
        }

        private bool IsTriangleSelectMode()
        {
            return _shiftKeyDown;
        }

        private void openTkControl_MouseEvent(MouseEventArgs e, MouseEventType eventType)
        {
            if (_inAnimationTab)
            {
                _selectedGizmo = Scene.GizmoId.None;
            }
            if (eventType == MouseEventType.Wheel)
            {
                _scene.CameraDistance -= e.Delta * MouseSensivity * _scene.CameraDistanceIncrement;
                _scene.UpdateViewMatrix();
                UpdateGizmos(_selectedGizmo, _hoveredGizmo, false);
                return;
            }
            var deltaX = e.X - _lastMouseX;
            var deltaY = e.Y - _lastMouseY;
            var mouseLeft = e.Button == MouseButtons.Left;
            var mouseMiddle = e.Button == MouseButtons.Middle;
            var mouseRight = e.Button == MouseButtons.Right;
            var selectedEntityBase = (EntityBase)_selectedRootEntity ?? _selectedModelEntity;
            var controlWidth = _openTkControl.Size.Width;
            var controlHeight = _openTkControl.Size.Height;
            _scene.UpdatePicking(e.Location.X, e.Location.Y, controlWidth, controlHeight);
            var hoveredGizmo = _scene.GetGizmoUnderPosition(selectedEntityBase);
            var selectedGizmo = _selectedGizmo;
            switch (_selectedGizmo)
            {
                case Scene.GizmoId.None:
                    if (!_inAnimationTab && mouseLeft && eventType == MouseEventType.Down)
                    {
                        _pickedPosition = _scene.GetPickedPosition(-_scene.CameraDirection);
                        if (hoveredGizmo == Scene.GizmoId.None)
                        {
                            var checkedEntities = GetCheckedEntities();
                            RootEntity rootEntity = null;
                            if (_selectedRootEntity != null)
                            {
                                rootEntity = _selectedRootEntity;
                            }
                            else if (_selectedModelEntity != null)
                            {
                                rootEntity = _selectedModelEntity.GetRootEntity();
                            }
                            if (IsTriangleSelectMode())
                            {
                                var newSelectedTriangle = _scene.GetTriangleUnderMouse(checkedEntities, rootEntity, e.Location.X, e.Location.Y, controlWidth, controlHeight);
                                if (newSelectedTriangle != null)
                                {
                                    SelectTriangle(newSelectedTriangle);
                                }
                                else
                                {
                                    UnselectTriangle();
                                }
                            }
                            else
                            {
                                var newSelectedEntity = _scene.GetEntityUnderMouse(checkedEntities, rootEntity, e.Location.X, e.Location.Y, controlWidth, controlHeight);
                                if (newSelectedEntity != null)
                                {
                                    SelectEntity(newSelectedEntity, false);
                                }
                                else
                                {
                                    UnselectTriangle();
                                }
                            }
                        }
                        else
                        {
                            selectedGizmo = hoveredGizmo;
                            _scene.ResetIntersection();
                        }
                    }
                    else
                    {
                        var hasToUpdateViewMatrix = false;
                        if (mouseRight && eventType == MouseEventType.Move)
                        {
                            _scene.CameraYaw -= deltaX * MouseSensivity;
                            _scene.CameraPitch += deltaY * MouseSensivity;
                            hasToUpdateViewMatrix = true;
                        }
                        if (mouseMiddle && eventType == MouseEventType.Move)
                        {
                            _scene.CameraX += deltaX * MouseSensivity * _scene.CameraPanIncrement;
                            _scene.CameraY += deltaY * MouseSensivity * _scene.CameraPanIncrement;
                            hasToUpdateViewMatrix = true;
                        }
                        if (hasToUpdateViewMatrix)
                        {
                            _scene.UpdateViewMatrix();
                            UpdateGizmos(_selectedGizmo, _hoveredGizmo, false);
                        }
                    }
                    break;
                case Scene.GizmoId.XMover when !_inAnimationTab:
                    if (mouseLeft && eventType == MouseEventType.Move && selectedEntityBase != null)
                    {
                        var pickedPosition = _scene.GetPickedPosition(-_scene.CameraDirection);
                        var projectedOffset = (pickedPosition - _pickedPosition).ProjectOnNormal(Vector3.UnitX);
                        selectedEntityBase.PositionX += projectedOffset.X;
                        selectedEntityBase.PositionY += projectedOffset.Y;
                        selectedEntityBase.PositionZ += projectedOffset.Z;
                        _pickedPosition = pickedPosition;
                        UpdateSelectedEntity(false);
                    }
                    else
                    {
                        AlignSelectedEntityToGrid(selectedEntityBase);
                        selectedGizmo = Scene.GizmoId.None;
                    }
                    break;
                case Scene.GizmoId.YMover when !_inAnimationTab:
                    if (mouseLeft && eventType == MouseEventType.Move && selectedEntityBase != null)
                    {
                        var pickedPosition = _scene.GetPickedPosition(-_scene.CameraDirection);
                        var projectedOffset = (pickedPosition - _pickedPosition).ProjectOnNormal(Vector3.UnitY);
                        selectedEntityBase.PositionX += projectedOffset.X;
                        selectedEntityBase.PositionY += projectedOffset.Y;
                        selectedEntityBase.PositionZ += projectedOffset.Z;
                        _pickedPosition = pickedPosition;
                        UpdateSelectedEntity(false);
                    }
                    else
                    {
                        AlignSelectedEntityToGrid(selectedEntityBase);
                        selectedGizmo = Scene.GizmoId.None;
                    }
                    break;
                case Scene.GizmoId.ZMover when !_inAnimationTab:
                    if (mouseLeft && eventType == MouseEventType.Move && selectedEntityBase != null)
                    {
                        var pickedPosition = _scene.GetPickedPosition(-_scene.CameraDirection);
                        var projectedOffset = (pickedPosition - _pickedPosition).ProjectOnNormal(Vector3.UnitZ);
                        selectedEntityBase.PositionX += projectedOffset.X;
                        selectedEntityBase.PositionY += projectedOffset.Y;
                        selectedEntityBase.PositionZ += projectedOffset.Z;
                        _pickedPosition = pickedPosition;
                        UpdateSelectedEntity(false);
                    }
                    else
                    {
                        AlignSelectedEntityToGrid(selectedEntityBase);
                        selectedGizmo = Scene.GizmoId.None;
                    }
                    break;
            }
            if (selectedGizmo != _selectedGizmo || hoveredGizmo != _hoveredGizmo)
            {
                UpdateGizmos(selectedGizmo, hoveredGizmo);
            }
            _lastMouseX = e.X;
            _lastMouseY = e.Y;
        }

        private void AlignSelectedEntityToGrid(EntityBase selectedEntityBase)
        {
            if (selectedEntityBase != null)
            {
                selectedEntityBase.PositionX = AlignToGrid(selectedEntityBase.PositionX);
                selectedEntityBase.PositionY = AlignToGrid(selectedEntityBase.PositionY);
                selectedEntityBase.PositionZ = AlignToGrid(selectedEntityBase.PositionZ);
                UpdateSelectedEntity(false);
            }
        }

        private void entitiesTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_selectionSource == EntitySelectionSource.None)
            {
                _selectionSource = EntitySelectionSource.TreeView;
            }
            var selectedNode = entitiesTreeView.SelectedNode;
            if (selectedNode != null)
            {
                _selectedRootEntity = selectedNode.Tag as RootEntity;
                _selectedModelEntity = selectedNode.Tag as ModelEntity;
                UnselectTriangle();
            }
            UpdateSelectedEntity();
        }

        private void entitiesTreeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            // handle unselecting triangle when clicking on a node in the tree view if that node is already selected.
            if (e.Node != null)
            {
                // Removed for now, because this also triggers when pressing
                // the expand button (which doesn't perform selection).
                //UnselectTriangle();
            }
        }

        private void UpdateGizmos(Scene.GizmoId selectedGizmo = Scene.GizmoId.None, Scene.GizmoId hoveredGizmo = Scene.GizmoId.None, bool updateMeshData = true)
        {
            if (updateMeshData)
            {
                _scene.GizmosMeshBatch.Reset(3);
            }
            var selectedEntityBase = (EntityBase)_selectedRootEntity ?? _selectedModelEntity;
            if (selectedEntityBase == null)
            {
                return;
            }
            var matrix = Matrix4.CreateTranslation(selectedEntityBase.Bounds3D.Center);
            var scaleMatrix = _scene.GetGizmoScaleMatrix(matrix.ExtractTranslation());
            var finalMatrix = scaleMatrix * matrix;
            _scene.GizmosMeshBatch.BindCube(finalMatrix, hoveredGizmo == Scene.GizmoId.XMover || selectedGizmo == Scene.GizmoId.XMover ? Classes.Color.White : Classes.Color.Red, Scene.XGizmoDimensions, Scene.XGizmoDimensions, 0, null, updateMeshData);
            _scene.GizmosMeshBatch.BindCube(finalMatrix, hoveredGizmo == Scene.GizmoId.YMover || selectedGizmo == Scene.GizmoId.YMover ? Classes.Color.White : Classes.Color.Green, Scene.YGizmoDimensions, Scene.YGizmoDimensions, 1, null, updateMeshData);
            _scene.GizmosMeshBatch.BindCube(finalMatrix, hoveredGizmo == Scene.GizmoId.ZMover || selectedGizmo == Scene.GizmoId.ZMover ? Classes.Color.White : Classes.Color.Blue, Scene.ZGizmoDimensions, Scene.ZGizmoDimensions, 2, null, updateMeshData);
            _selectedGizmo = selectedGizmo;
            _hoveredGizmo = hoveredGizmo;
        }

        private void UpdateSelectedEntity(bool updateMeshData = true)
        {
            _scene.BoundsBatch.Reset();
            _scene.SkeletonBatch.Reset();
            var selectedEntityBase = (EntityBase)_selectedRootEntity ?? _selectedModelEntity;
            var rootEntity = selectedEntityBase?.GetRootEntity();
            if (rootEntity != null)
            {
                rootEntity.ResetAnimationData();
                rootEntity.FixConnections();
            }
            if (selectedEntityBase != null)
            {
                selectedEntityBase.ComputeBoundsRecursively();
                var checkedEntities = GetCheckedEntities();
                _scene.BoundsBatch.SetupEntityBounds(selectedEntityBase);
                _scene.MeshBatch.SetupMultipleEntityBatch(checkedEntities, _selectedModelEntity, _selectedRootEntity, _scene.TextureBinder, updateMeshData || _scene.AutoAttach, _selectionSource == EntitySelectionSource.TreeView && _selectedModelEntity == null);
            }
            else
            {
                _scene.MeshBatch.Reset(0);
                _selectedGizmo = Scene.GizmoId.None;
                _hoveredGizmo = Scene.GizmoId.None;
            }
            UpdateSelectedTriangle();
            UpdateModelPropertyGrid();
            UpdateGizmos(_selectedGizmo, _hoveredGizmo, updateMeshData);
            _selectionSource = EntitySelectionSource.None;
        }

        private void UpdateSelectedTriangle()
        {
            _scene.TriangleOutlineBatch.Reset();
            if (_selectedTriangle != null)
            {
                _scene.TriangleOutlineBatch.SetupTriangleOutline(_selectedTriangle.Item2, _selectedTriangle.Item1.WorldMatrix);
            }
        }

        private void UpdateModelPropertyGrid()
        {
            var selectedEntityBase = (EntityBase)_selectedRootEntity ?? _selectedModelEntity;

            object propertyObject = null;
            if (_selectedTriangle != null)
            {
                propertyObject = _selectedTriangle.Item2;
            }
            else if (selectedEntityBase != null)
            {
                propertyObject = selectedEntityBase;
            }
            modelPropertyGrid.SelectedObject = propertyObject;
        }

        private void UpdateSelectedAnimation()
        {
            var selectedObject = _curAnimationFrameObj ?? _curAnimationObject ?? (object)_curAnimation;
            if (selectedObject == null)
            {
                return;
            }
            if (_curAnimation != null)
            {
                _curAnimationTime = 0f;
                _curAnimationFrame = 0f;
                UpdateAnimationFPS();
                animationPlayButton.Enabled = true;
                Playing = false;
                UpdateFrameLabel();
            }
            animationPropertyGrid.SelectedObject = selectedObject;
            _scene.AnimationBatch.SetupAnimationBatch(_curAnimation);
        }

        private void UpdateAnimationFPS()
        {
            _animateTimer.Interval = 1f / 60f * (animationSpeedTrackbar.Value / 100f);
        }

        private Texture GetSelectedTexture(int? index = null)
        {
            if (!index.HasValue && texturesListView.SelectedIndices.Count == 0)
            {
                return null;
            }
            var textureIndex = index ?? texturesListView.SelectedIndices[0];
            if (textureIndex < 0)
            {
                return null;
            }
            return _textures[textureIndex];
        }

        private void drawToVRAMButton_Click(object sender, EventArgs e)
        {
            var selectedIndices = texturesListView.SelectedIndices;
            if (selectedIndices.Count == 0)
            {
                MessageBox.Show("Select the textures to draw to VRAM first");
                return;
            }
            foreach (int index in texturesListView.SelectedIndices)
            {
                _vram.DrawTexture(GetSelectedTexture(index), true); // Suppress updates to scene until all textures are drawn.
            }
            _vram.UpdateAllPages();
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            var index = vramComboBox.SelectedIndex;
            if (index > -1)
            {
                vramPagePictureBox.Image = _vram[index].Bitmap;
            }
        }

        private void modelPropertyGrid_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            var selectedNode = entitiesTreeView.SelectedNode;
            if (selectedNode == null)
            {
                return;
            }
            if (_selectedModelEntity != null)
            {
                _selectedModelEntity.TexturePage = VRAMPages.ClampTexturePage(_selectedModelEntity.TexturePage);
                _selectedModelEntity.Texture = _vram[_selectedModelEntity.TexturePage];
            }
            var selectedEntityBase = (EntityBase)_selectedRootEntity ?? _selectedModelEntity;
            if (selectedEntityBase != null)
            {
                selectedNode.Text = selectedEntityBase.EntityName;
                selectedEntityBase.PositionX = AlignToGrid(selectedEntityBase.PositionX);
                selectedEntityBase.PositionY = AlignToGrid(selectedEntityBase.PositionY);
                selectedEntityBase.PositionZ = AlignToGrid(selectedEntityBase.PositionZ);
            }
            UpdateSelectedEntity(false);
        }

        private float AlignToGrid(float value)
        {
            return (float)((int)(value / (float)gridSizeNumericUpDown.Value) * gridSizeNumericUpDown.Value);
        }

        private void texturePropertyGrid_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            var selectedNodes = texturesListView.SelectedItems;
            if (selectedNodes.Count == 0)
            {
                return;
            }
            var selectedNode = selectedNodes[0];
            if (selectedNode == null)
            {
                return;
            }
            var texture = GetSelectedTexture();
            if (texture == null)
            {
                return;
            }
            texture.X = VRAMPages.ClampTextureX(texture.X);
            texture.Y = VRAMPages.ClampTextureY(texture.Y);
            texture.TexturePage = VRAMPages.ClampTexturePage(texture.TexturePage);
            selectedNode.Text = texture.TextureName;
        }

        private void btnClearPage_Click(object sender, EventArgs e)
        {
            var index = vramComboBox.SelectedIndex;
            if (index <= -1)
            {
                MessageBox.Show("Select a page first");
                return;
            }
            ClearPage(index);
            MessageBox.Show("Page cleared");
        }

        private void ClearPage(int index)
        {
            _vram.ClearPage(index);
            vramPagePictureBox.Image = _vram[index].Bitmap;
        }

        private void cmsModelExport_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var checkedEntities = GetCheckedEntities();
            if (checkedEntities == null)
            {
                MessageBox.Show("Check the models to export first");
                return;
            }
            string path;
            if (ShowEntityFolderSelect(out path) == DialogResult.OK)
            {
                if (e.ClickedItem == miOBJ)
                {
                    var objExporter = new ObjExporter();
                    objExporter.Export(checkedEntities, path);
                }
                if (e.ClickedItem == miOBJVC)
                {
                    var objExporter = new ObjExporter();
                    objExporter.Export(checkedEntities, path, true);
                }
                if (e.ClickedItem == miOBJMerged)
                {
                    var objExporter = new ObjExporter();
                    objExporter.Export(checkedEntities, path, false, true);
                }
                if (e.ClickedItem == miOBJVCMerged)
                {
                    var objExporter = new ObjExporter();
                    objExporter.Export(checkedEntities, path, true, true);
                }
                MessageBox.Show("Models exported");
            }
        }

        private void findByPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var pageIndexString = Utils.ShowDialog("Find by Page", "Type the page number");
            int pageIndex;
            if (string.IsNullOrEmpty(pageIndexString))
            {
                return;
            }
            if (!int.TryParse(pageIndexString, out pageIndex))
            {
                MessageBox.Show("Invalid page number");
                return;
            }
            var found = 0;
            for (var i = 0; i < texturesListView.Items.Count; i++)
            {
                var item = texturesListView.Items[i];
                item.Group = null;
                var texture = _textures[i];
                if (texture.TexturePage != pageIndex)
                {
                    continue;
                }
                item.Group = texturesListView.Groups[0];
                found++;
            }
            MessageBox.Show(found > 0 ? $"Found {found} items" : "Nothing found");
        }

        private void texturesListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (texturesListView.SelectedIndices.Count == 0 || texturesListView.SelectedIndices.Count > 1)
            {
                texturePropertyGrid.SelectedObject = null;
                return;
            }
            var texture = GetSelectedTexture();
            if (texture == null)
            {
                return;
            }
            _texturePreviewScale = 1f;
            var bitmap = texture.Bitmap;
            texturePreviewPictureBox.Image = bitmap;
            texturePreviewPictureBox.Width = bitmap.Width;
            texturePreviewPictureBox.Height = bitmap.Height;
            texturePreviewPictureBox.Refresh();
            texturePropertyGrid.SelectedObject = texture;
        }

        private void DrawUV(EntityBase entity, Graphics graphics)
        {
            if (entity == null)
            {
                return;
            }
            if (entity is ModelEntity modelEntity) // && modelEntity.HasUvs)
            {
                foreach (var triangle in modelEntity.Triangles)
                {
                    graphics.DrawLine(Black3Px, triangle.Uv[0].X * 255f, triangle.Uv[0].Y * 255f, triangle.Uv[1].X * 255f, triangle.Uv[1].Y * 255f);
                    graphics.DrawLine(Black3Px, triangle.Uv[1].X * 255f, triangle.Uv[1].Y * 255f, triangle.Uv[2].X * 255f, triangle.Uv[2].Y * 255f);
                    graphics.DrawLine(Black3Px, triangle.Uv[2].X * 255f, triangle.Uv[2].Y * 255f, triangle.Uv[0].X * 255f, triangle.Uv[0].Y * 255f);
                }
            }
            if (entity is ModelEntity modelEntity2) //&& modelEntity2.HasUvs)
            {
                foreach (var triangle in modelEntity2.Triangles)
                {
                    graphics.DrawLine(White1Px, triangle.Uv[0].X * 255f, triangle.Uv[0].Y * 255f, triangle.Uv[1].X * 255f, triangle.Uv[1].Y * 255f);
                    graphics.DrawLine(White1Px, triangle.Uv[1].X * 255f, triangle.Uv[1].Y * 255f, triangle.Uv[2].X * 255f, triangle.Uv[2].Y * 255f);
                    graphics.DrawLine(White1Px, triangle.Uv[2].X * 255f, triangle.Uv[2].Y * 255f, triangle.Uv[0].X * 255f, triangle.Uv[0].Y * 255f);
                }
            }
            if (entity.ChildEntities == null)
            {
                return;
            }
            foreach (var subEntity in entity.ChildEntities)
            {
                DrawUV(subEntity, graphics);
            }
        }

        private void clearSearchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (var i = 0; i < texturesListView.Items.Count; i++)
            {
                var item = texturesListView.Items[i];
                item.Group = null;
            }
            MessageBox.Show("Results cleared");
        }

        private void wireframeToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            _scene.Wireframe = wireframeToolStripMenuItem.Checked;
        }

        private void clearAllPagesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _vram.ClearAllPages();
            MessageBox.Show("Pages cleared");
        }

        private void openTKControl_Load(object sender, EventArgs e)
        {
            SetupScene();
            SetupColors();
            SetupVRAM();
            SetupEntities();
            SetupTextures();
            SetupAnimations();
        }

        private void SetMaskColor(Color color)
        {
            _scene.MaskColor = color;
            if (_maskColorBitmap == null)
            {
                _maskColorBitmap = new Bitmap(16, 16);
            }
            using (var graphics = Graphics.FromImage(_maskColorBitmap))
            {
                graphics.Clear(color);
            }
            setMaskColorToolStripMenuItem.Image = _maskColorBitmap;
        }

        private void SetAmbientColor(Color color)
        {
            _scene.AmbientColor = color;
            if (_ambientColorBitmap == null)
            {
                _ambientColorBitmap = new Bitmap(16, 16);
            }
            using (var graphics = Graphics.FromImage(_ambientColorBitmap))
            {
                graphics.Clear(color);
            }
            setAmbientColorToolStripMenuItem.Image = _ambientColorBitmap;
        }

        private void SetBackgroundColor(Color color)
        {
            _scene.ClearColor = new Classes.Color(color.R / 255f, color.G / 255f, color.B / 255f);
            if (_backgroundColorBitmap == null)
            {
                _backgroundColorBitmap = new Bitmap(16, 16);
            }
            using (var graphics = Graphics.FromImage(_backgroundColorBitmap))
            {
                graphics.Clear(color);
            }
            setBackgroundColorToolStripMenuItem.Image = _backgroundColorBitmap;
        }

        private void Redraw()
        {
            _openTkControl.Invalidate();
        }

        private void menusTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (menusTabControl.SelectedTab.TabIndex)
            {
                case 0:
                    _inAnimationTab = false;
                    animationsTreeView.SelectedNode = null;
                    _openTkControl.Parent = modelsSplitContainer.Panel2;
                    _openTkControl.Show();
                    break;
                case 3:
                    _inAnimationTab = true;
                    _openTkControl.Parent = animationsSplitContainer.Panel2;
                    _openTkControl.Show();
                    UpdateSelectedAnimation();
                    break;
                default:
                    _openTkControl.Parent = null;
                    _openTkControl.Hide();
                    break;
            }
        }

        private void animationsTreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            UpdateSelectedEntity();
            var selectedNode = animationsTreeView.SelectedNode;
            if (selectedNode == null)
            {
                return;
            }
            if (selectedNode.Tag is Animation animation)
            {
                _curAnimation = animation;
                _curAnimationObject = null;
                _curAnimationFrameObj = null;
            }
            if (selectedNode.Tag is AnimationObject animationObject)
            {
                _curAnimation = animationObject.Animation;
                _curAnimationObject = animationObject;
                _curAnimationFrameObj = null;
            }
            if (selectedNode.Tag is AnimationFrame)
            {
                _curAnimationFrameObj = (AnimationFrame)selectedNode.Tag;
                _curAnimationObject = _curAnimationFrameObj.AnimationObject;
                _curAnimation = _curAnimationFrameObj.AnimationObject.Animation;
                UpdateFrameLabel();
            }
            UpdateSelectedAnimation();
            if (_curAnimationFrameObj != null)
            {
                _curAnimationFrame = _curAnimationFrameObj.FrameTime + 0.9999f;
            }
        }

        private void animationPropertyGrid_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            UpdateSelectedEntity();
            UpdateSelectedAnimation();
        }

        private void animationPlayButton_Click(object sender, EventArgs e)
        {
            Playing = !Playing;
        }

        public void UpdateProgress(int value, int max, bool complete, string message)
        {
            if (InvokeRequired)
            {
                var invokeAction = new Action<int, int, bool, string>(UpdateProgress);
                Invoke(invokeAction, value, max, complete, message);
            }
            else
            {
                toolStripProgressBar1.Minimum = 0;
                toolStripProgressBar1.Maximum = max;
                toolStripProgressBar1.Value = value;
                toolStripProgressBar1.Enabled = !complete;
                toolStripStatusLabel1.Text = message;
            }
        }

        public void ReloadItems()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ReloadItems));
            }
            else
            {
                _refreshAction(this);
                Redraw();
            }
        }

        private void vramPagePictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (!_showUv)
            {
                return;
            }
            var checkedEntities = GetCheckedEntities();
            if (checkedEntities != null)
            {
                foreach (var checkedEntity in checkedEntities)
                {
                    if (checkedEntity == _selectedRootEntity)
                    {
                        continue;
                    }
                    DrawUV(checkedEntity, e.Graphics);
                }
            }
            DrawUV(_selectedRootEntity, e.Graphics);
        }

        private void entitiesTreeView_AfterCheck(object sender, TreeViewEventArgs e)
        {
            UpdateSelectedEntity();
        }

        private void showGizmosToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _scene.ShowGizmos = showGizmosToolStripMenuItem.Checked;
        }

        private void showBoundsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _scene.ShowBounds = showBoundsToolStripMenuItem.Checked;
        }

        private void showUVToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _showUv = showUVToolStripMenuItem.Checked;
            vramPagePictureBox.Refresh();
        }

        private void showSkeletonToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            _scene.ShowSkeleton = showSkeletonToolStripMenuItem.Checked;
        }

        private void animationSpeedTrackbar_Scroll(object sender, EventArgs e)
        {
            UpdateAnimationFPS();
        }

        private void lightRoll_Scroll(object sender, EventArgs e)
        {
            UpdateLightDirection();
        }

        private void lightYaw_Scroll(object sender, EventArgs e)
        {
            UpdateLightDirection();
        }

        private void lightPitch_Scroll(object sender, EventArgs e)
        {
            UpdateLightDirection();
        }

        private void UpdateLightDirection()
        {
            _scene.LightRotation = new Vector3(MathHelper.DegreesToRadians((float)lightPitchNumericUpDown.Value), MathHelper.DegreesToRadians((float)lightYawNumericUpDown.Value), MathHelper.DegreesToRadians((float)lightRollNumericUpDown.Value));
        }

        private void restartToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
            Program.Initialize(null);
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("PSXPrev - Playstation (PSX) Files Previewer/Extractor\n" + "(c) PSX Prev Contributors - 2020-2023", "About");
        }

        private void videoTutorialToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://www.youtube.com/watch?v=hPDa8l3ZE6U");
        }

        private void autoAttachLimbsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _scene.AutoAttach = autoAttachLimbsToolStripMenuItem.Checked;
            UpdateSelectedEntity();
        }

        private void compatibilityListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://docs.google.com/spreadsheets/d/155pUzwl7CC14ssT0PJkaEA53CS1ijpOV04VitQCVBC4/edit?pli=1#gid=22642205");
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void lightPitchNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            UpdateLightDirection();
        }

        private void lightYawNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            UpdateLightDirection();
        }

        private void lightRollNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            UpdateLightDirection();
        }

        private void setMaskColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog())
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    SetMaskColor(colorDialog.Color);
                }
            }
        }

        private void enableLightToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _scene.LightEnabled = enableLightToolStripMenuItem.Checked;
        }


        private void setAmbientColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog())
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    SetAmbientColor(colorDialog.Color);
                }
            }
        }

        private void setBackgroundColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog())
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    SetBackgroundColor(colorDialog.Color);
                }
            }
        }

        private void lightIntensityNumericUpDown_ValueChanged(object sender, EventArgs e)
        {
            _scene.LightIntensity = (float)lightIntensityNumericUpDown.Value / 100f;
        }

        private void vibRibbonWireframeToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void wireframeToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void lineRendererToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            _scene.VibRibbonWireframe = lineRendererToolStripMenuItem.Checked;
        }

        private void resetWholeModelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedEntityBase = (EntityBase)_selectedRootEntity ?? _selectedModelEntity;
            if (selectedEntityBase != null)
            {
                // This could be changed to only reset the selected model and its children.
                // But that's only necessary if sub-sub-model support is ever added.
                selectedEntityBase.GetRootEntity()?.ResetTransform(true);
                UpdateSelectedEntity();
            }
        }

        private void resetSelectedModelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var selectedEntityBase = (EntityBase)_selectedRootEntity ?? _selectedModelEntity;
            if (selectedEntityBase != null)
            {
                selectedEntityBase.ResetTransform(false);
                UpdateSelectedEntity();
            }
        }

        private enum MouseEventType
        {
            Down,
            Up,
            Move,
            Wheel
        }

        private enum KeyEventType
        {
            Down,
            Up,
        }

        private enum EntitySelectionSource
        {
            None,
            TreeView,
            Click
        }

        private void statusStrip1_Resize(object sender, EventArgs e)
        {
            ResizeToolStrip();
        }

        private void ResizeToolStrip()
        {
            toolStripStatusLabel1.Width = (int)(statusStrip1.Width * 0.35f);
            toolStripProgressBar1.Width = (int)(statusStrip1.Width * 0.65f);
        }

        private void enableTransparencyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _scene.SemiTransparencyEnabled = enableTransparencyToolStripMenuItem.Checked;
            Redraw();
        }

        private void forceDoubleSidedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _scene.ForceDoubleSided = forceDoubleSidedToolStripMenuItem.Checked;
            Redraw();
        }

        private void texturePreviewPictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (texturePreviewPictureBox.Image == null)
            {
                return;
            }
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.DrawImage(
                texturePreviewPictureBox.Image,
                new Rectangle(0, 0, texturePreviewPictureBox.Width, texturePreviewPictureBox.Height),
                0,
                0, 
                texturePreviewPictureBox.Image.Width,  
                texturePreviewPictureBox.Image.Height, 
                GraphicsUnit.Pixel);
        }

        private void verticesOnlyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _scene.VerticesOnly = verticesOnlyToolStripMenuItem.Checked;
        }

        private void vertexSizeUpDown_ValueChanged(object sender, EventArgs e)
        {
            _scene.VertexSize = vertexSizeUpDown.Value;
        }

        private void pauseScanningToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            Program.HaltRequested = pauseScanningToolStripMenuItem.Checked;
        }

        private void previewForm_KeyDown(object sender, KeyEventArgs e)
        {
            previewForm_KeyEvent(e, KeyEventType.Down);
        }

        private void previewForm_KeyUp(object sender, KeyEventArgs e)
        {
            previewForm_KeyEvent(e, KeyEventType.Up);
        }

        private void previewForm_KeyEvent(KeyEventArgs e, KeyEventType eventType)
        {
            if (eventType == KeyEventType.Down || eventType == KeyEventType.Up)
            {
                var state = eventType == KeyEventType.Down;
                switch (e.KeyCode)
                {
                    case Keys.ShiftKey:
                        _shiftKeyDown = state;
                        break;
                    case Keys.ControlKey:
                        _controlKeyDown = state;
                        break;
                }
            }
        }
    }
}