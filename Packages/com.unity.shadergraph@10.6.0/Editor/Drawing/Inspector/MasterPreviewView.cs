using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.Graphing.Util;
using UnityEditor.ShaderGraph.Internal;
using Object = UnityEngine.Object;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using UnityEditor.ShaderGraph.Drawing;
using UnityEditor.ShaderGraph;

namespace UnityEditor.ShaderGraph.Drawing.Inspector
{
    class MasterPreviewView : VisualElement
    {
        PreviewManager m_PreviewManager;
        GraphData m_Graph;

        PreviewRenderData m_PreviewRenderHandle;
        Image m_PreviewTextureView;

        public Image previewTextureView
        {
            get { return m_PreviewTextureView; }
        }

        Vector2 m_PreviewScrollPosition;
        ObjectField m_PreviewMeshPicker;

        Mesh m_PreviousMesh;

        bool m_RecalculateLayout;

        ResizeBorderFrame m_PreviewResizeBorderFrame;

        public ResizeBorderFrame previewResizeBorderFrame
        {
            get { return m_PreviewResizeBorderFrame; }
        }

        VisualElement m_Preview;
        Label m_Title;

        public VisualElement preview
        {
            get { return m_Preview; }
        }

        List<string> m_DoNotShowPrimitives = new List<string>(new string[] {PrimitiveType.Plane.ToString()});

        static Type s_ContextualMenuManipulator = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypesOrNothing()).FirstOrDefault(t => t.FullName == "UnityEngine.UIElements.ContextualMenuManipulator");
        static Type s_ObjectSelector = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypesOrNothing()).FirstOrDefault(t => t.FullName == "UnityEditor.ObjectSelector");

        public string assetName
        {
            get { return m_Title.text; }
            set { m_Title.text = value; }
        }

        public MasterPreviewView(PreviewManager previewManager, GraphData graph)
        {
            //Debug.Log("The Main Preview is being built here");
            style.overflow = Overflow.Hidden;
            m_PreviewManager = previewManager;
            m_Graph = graph;

            styleSheets.Add(Resources.Load<StyleSheet>("Styles/MasterPreviewView"));
            
            m_PreviewRenderHandle = previewManager.masterRenderData;
            if (m_PreviewRenderHandle != null)
            {
                m_PreviewRenderHandle.onPreviewChanged += OnPreviewChanged;
            }

            var topContainer = new VisualElement() { name = "top" };
            {
                m_Title = new Label() { name = "title" };
                m_Title.text = "Main Preview";

                topContainer.Add(m_Title);
            }
            Add(topContainer);

            m_Preview = new VisualElement {name = "middle"};
            {
                m_PreviewTextureView = CreatePreview(Texture2D.blackTexture);
                m_PreviewScrollPosition = new Vector2(0f, 0f);
                preview.Add(m_PreviewTextureView);
                preview.AddManipulator(new Scrollable(OnScroll));
            }
            Add(preview);

            m_PreviewResizeBorderFrame = new ResizeBorderFrame(this, this) { name = "resizeBorderFrame" };
            m_PreviewResizeBorderFrame.maintainAspectRatio = true;
            Add(m_PreviewResizeBorderFrame);

            m_RecalculateLayout = false;
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        Image CreatePreview(Texture texture)
        {
            //Debug.Log("Creating preview");
            if (m_PreviewRenderHandle?.texture != null)
            {
                texture = m_PreviewRenderHandle.texture;
            }

            var image = new Image { name = "preview", image = texture };
            image.AddManipulator(new Draggable(OnMouseDragPreviewMesh, true));
            image.AddManipulator((IManipulator)Activator.CreateInstance(s_ContextualMenuManipulator, (Action<ContextualMenuPopulateEvent>)BuildContextualMenu));
            return image;
        }

        void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            foreach (var primitiveTypeName in Enum.GetNames(typeof(PrimitiveType)))
            {
                if (m_DoNotShowPrimitives.Contains(primitiveTypeName))
                    continue;
                evt.menu.AppendAction(primitiveTypeName, e => ChangePrimitiveMesh(primitiveTypeName), DropdownMenuAction.AlwaysEnabled);
            }

            evt.menu.AppendAction("Custom Mesh", e => ChangeMeshCustom(), DropdownMenuAction.AlwaysEnabled);
        }

        void OnPreviewChanged()
        {
            m_PreviewTextureView.image = m_PreviewRenderHandle?.texture ?? Texture2D.blackTexture;
            if (m_PreviewRenderHandle != null && m_PreviewRenderHandle.shaderData.isOutOfDate)
                m_PreviewTextureView.tintColor = new Color(1.0f, 1.0f, 1.0f, 0.3f);
            else
                m_PreviewTextureView.tintColor = Color.white;
            m_PreviewTextureView.MarkDirtyRepaint();
        }

        void ChangePrimitiveMesh(string primitiveName)
        {
            Mesh changedPrimitiveMesh = Resources.GetBuiltinResource(typeof(Mesh), string.Format("{0}.fbx", primitiveName)) as Mesh;

            ChangeMesh(changedPrimitiveMesh);
        }

        void ChangeMesh(Mesh mesh)
        {
            Mesh changedMesh = mesh;

            m_PreviewManager.UpdateMasterPreview(ModificationScope.Node);

            if (m_Graph.previewData.serializedMesh.mesh != changedMesh)
            {
                m_Graph.previewData.rotation = Quaternion.identity;
                m_PreviewScrollPosition = Vector2.zero;
            }

            m_Graph.previewData.serializedMesh.mesh = changedMesh;
        }

        private static EditorWindow Get()
        {
            PropertyInfo P = s_ObjectSelector.GetProperty("get", BindingFlags.Public | BindingFlags.Static);
            return P.GetValue(null, null) as EditorWindow;
        }

        void OnMeshChanged(Object obj)
        {
            var mesh = obj as Mesh;
            if (mesh == null)
                mesh = m_PreviousMesh;
            ChangeMesh(mesh);
        }

        void ChangeMeshCustom()
        {
            MethodInfo ShowMethod = s_ObjectSelector.GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, Type.DefaultBinder, new[] {typeof(Object), typeof(Type), typeof(Object), typeof(bool), typeof(List<int>), typeof(Action<Object>), typeof(Action<Object>)}, new ParameterModifier[7]);
            m_PreviousMesh = m_Graph.previewData.serializedMesh.mesh;
            ShowMethod.Invoke(Get(), new object[] { null, typeof(Mesh), null, false, null, (Action<Object>)OnMeshChanged, (Action<Object>)OnMeshChanged });
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            //Debug.Log("Changed Geometry");
            if (m_RecalculateLayout)
            {
                WindowDockingLayout dockingLayout = new WindowDockingLayout();
                dockingLayout.CalculateDockingCornerAndOffset(layout, parent.layout);
                dockingLayout.ClampToParentWindow();
                dockingLayout.ApplyPosition(this);
                m_RecalculateLayout = false;
            }

            var currentWidth = m_PreviewRenderHandle?.texture != null ? m_PreviewRenderHandle.texture.width : -1;
            var currentHeight = m_PreviewRenderHandle?.texture != null ? m_PreviewRenderHandle.texture.height : -1;

            var targetWidth = Mathf.Max(1f, m_PreviewTextureView.contentRect.width);
            var targetHeight = Mathf.Max(1f, m_PreviewTextureView.contentRect.height);

            if (Mathf.Approximately(currentWidth, targetHeight) && Mathf.Approximately(currentHeight, targetWidth))
                return;

            m_PreviewTextureView.style.width = evt.newRect.width;
            m_PreviewTextureView.style.height = evt.newRect.height - 40.0f;
            m_PreviewManager.ResizeMasterPreview(new Vector2(evt.newRect.width, evt.newRect.width));
        }

        void OnScroll(float scrollValue)
        {
            float rescaleAmount = -scrollValue * .03f;
            m_Graph.previewData.scale = Mathf.Clamp(m_Graph.previewData.scale + rescaleAmount, 0.2f, 5f);

            m_PreviewManager.UpdateMasterPreview(ModificationScope.Node);
        }

        void OnMouseDragPreviewMesh(Vector2 deltaMouse)
        {
            Vector2 previewSize = m_PreviewTextureView.contentRect.size;

            m_PreviewScrollPosition -= deltaMouse * (Event.current.shift ? 3f : 1f) / Mathf.Min(previewSize.x, previewSize.y) * 140f;
            m_PreviewScrollPosition.y = Mathf.Clamp(m_PreviewScrollPosition.y, -90f, 90f);
            Quaternion previewRotation = Quaternion.Euler(m_PreviewScrollPosition.y, 0, 0) * Quaternion.Euler(0, m_PreviewScrollPosition.x, 0);
            m_Graph.previewData.rotation = previewRotation;

            m_PreviewManager.UpdateMasterPreview(ModificationScope.Node);
        }
    }
}

namespace AutoGen
{
    
    class MasterShaderPreview : VisualElement, IDisposable
    {
        ShaderPreviewManager m_PreviewManager;
        GraphData m_Graph;

        ShaderPrevRenderData m_PreviewRenderHandle;
        Image m_PreviewTextureView;

        public Image previewTextureView
        {
            get { return m_PreviewTextureView; }
        }

        Vector2 m_PreviewScrollPosition;
        ObjectField m_PreviewMeshPicker;

        Mesh m_PreviousMesh;

        bool m_RecalculateLayout;

        ResizeBorderFrame m_PreviewResizeBorderFrame;

        public ResizeBorderFrame previewResizeBorderFrame
        {
            get { return m_PreviewResizeBorderFrame; }
        }

        VisualElement m_Preview;
        Label m_Title;

        public VisualElement preview
        {
            get { return m_Preview; }
        }

        List<string> m_DoNotShowPrimitives = new List<string>(new string[] { PrimitiveType.Plane.ToString() });

        static Type s_ContextualMenuManipulator = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypesOrNothing()).FirstOrDefault(t => t.FullName == "UnityEngine.UIElements.ContextualMenuManipulator");
        static Type s_ObjectSelector = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypesOrNothing()).FirstOrDefault(t => t.FullName == "UnityEditor.ObjectSelector");

        public string assetName
        {
            get { return m_Title.text; }
            set { m_Title.text = value; }
        }

        public MasterShaderPreview(ShaderPreviewManager previewManager, GraphData graph)
        {
            style.overflow = Overflow.Hidden;
            m_PreviewManager = previewManager;
            m_Graph = graph;

            styleSheets.Add(Resources.Load<StyleSheet>("Styles/MasterShaderPreview"));

            m_PreviewRenderHandle = previewManager.masterRenderData;
            if (m_PreviewRenderHandle != null)
            {
                m_PreviewRenderHandle.onPreviewChanged += OnPreviewChanged;
            }

            var topContainer = new VisualElement() { name = "top" };
            {
                m_Title = new Label() { name = "title" };
                m_Title.text = "Preview";

                topContainer.Add(m_Title);
            }
            Add(topContainer);

            m_Preview = new VisualElement { name = "middle" };
            {
                m_PreviewTextureView = CreatePreview(Texture2D.blackTexture);
                m_PreviewScrollPosition = new Vector2(0f, 0f);
                preview.Add(m_PreviewTextureView);
                preview.AddManipulator(new Scrollable(OnScroll));
            }
            Add(preview);

            m_PreviewResizeBorderFrame = new ResizeBorderFrame(this, this) { name = "resizeBorderFrame" };
            m_PreviewResizeBorderFrame.maintainAspectRatio = true;
            Add(m_PreviewResizeBorderFrame);

            m_RecalculateLayout = false;
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        

        Image CreatePreview(Texture texture)
        {
            //Debug.Log("Creating preview");
            if (m_PreviewRenderHandle?.texture != null)
            {
                texture = m_PreviewRenderHandle.texture;
            }

            var image = new Image { name = "preview", image = texture };
            image.AddManipulator(new Draggable(OnMouseDragPreviewMesh, true));
            image.AddManipulator((IManipulator)Activator.CreateInstance(s_ContextualMenuManipulator, (Action<ContextualMenuPopulateEvent>)BuildContextualMenu));
            return image;
        }

        void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            foreach (var primitiveTypeName in Enum.GetNames(typeof(PrimitiveType)))
            {
                if (m_DoNotShowPrimitives.Contains(primitiveTypeName))
                    continue;
                evt.menu.AppendAction(primitiveTypeName, e => ChangePrimitiveMesh(primitiveTypeName), DropdownMenuAction.AlwaysEnabled);
            }

            evt.menu.AppendAction("Custom Mesh", e => ChangeMeshCustom(), DropdownMenuAction.AlwaysEnabled);
        }

        void OnPreviewChanged()
        {
            m_PreviewTextureView.image = m_PreviewRenderHandle?.texture ?? Texture2D.blackTexture;
            if (m_PreviewRenderHandle != null && m_PreviewRenderHandle.shaderData.isOutOfDate)
                m_PreviewTextureView.tintColor = new Color(1.0f, 1.0f, 1.0f, 0.3f);
            else
                m_PreviewTextureView.tintColor = Color.white;
            m_PreviewTextureView.MarkDirtyRepaint();
        }

        void ChangePrimitiveMesh(string primitiveName)
        {
            Mesh changedPrimitiveMesh = Resources.GetBuiltinResource(typeof(Mesh), string.Format("{0}.fbx", primitiveName)) as Mesh;

            ChangeMesh(changedPrimitiveMesh);
        }

        void ChangeMesh(Mesh mesh)
        {
            Mesh changedMesh = mesh;

            m_PreviewManager.UpdateMasterPreview(ModificationScope.Node);

            if (m_Graph.previewData.serializedMesh.mesh != changedMesh)
            {
                m_Graph.previewData.rotation = Quaternion.identity;
                m_PreviewScrollPosition = Vector2.zero;
            }

            m_Graph.previewData.serializedMesh.mesh = changedMesh;
        }

        private static EditorWindow Get()
        {
            PropertyInfo P = s_ObjectSelector.GetProperty("get", BindingFlags.Public | BindingFlags.Static);
            return P.GetValue(null, null) as EditorWindow;
        }

        void OnMeshChanged(Object obj)
        {
            var mesh = obj as Mesh;
            if (mesh == null)
                mesh = m_PreviousMesh;
            ChangeMesh(mesh);
        }

        void ChangeMeshCustom()
        {
            MethodInfo ShowMethod = s_ObjectSelector.GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, Type.DefaultBinder, new[] { typeof(Object), typeof(Type), typeof(Object), typeof(bool), typeof(List<int>), typeof(Action<Object>), typeof(Action<Object>) }, new ParameterModifier[7]);
            m_PreviousMesh = m_Graph.previewData.serializedMesh.mesh;
            ShowMethod.Invoke(Get(), new object[] { null, typeof(Mesh), null, false, null, (Action<Object>)OnMeshChanged, (Action<Object>)OnMeshChanged });
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            //Debug.Log("Changed Geometry");
            if (m_RecalculateLayout)
            {
                WindowDockingLayout dockingLayout = new WindowDockingLayout();
                dockingLayout.CalculateDockingCornerAndOffset(layout, parent.layout);
                dockingLayout.ClampToParentWindow();
                dockingLayout.ApplyPosition(this);
                m_RecalculateLayout = false;
            }

            var currentWidth = m_PreviewRenderHandle?.texture != null ? m_PreviewRenderHandle.texture.width : -1;
            var currentHeight = m_PreviewRenderHandle?.texture != null ? m_PreviewRenderHandle.texture.height : -1;

            var targetWidth = Mathf.Max(1f, m_PreviewTextureView.contentRect.width);
            var targetHeight = Mathf.Max(1f, m_PreviewTextureView.contentRect.height);

            if (Mathf.Approximately(currentWidth, targetHeight) && Mathf.Approximately(currentHeight, targetWidth))
                return;

            m_PreviewTextureView.style.width = evt.newRect.width;
            m_PreviewTextureView.style.height = evt.newRect.height - 40.0f;
            m_PreviewManager.ResizeMasterPreview(new Vector2(evt.newRect.width, evt.newRect.width));
        }

        void OnScroll(float scrollValue)
        {
            float rescaleAmount = -scrollValue * .03f;
            m_Graph.previewData.scale = Mathf.Clamp(m_Graph.previewData.scale + rescaleAmount, 0.2f, 5f);

            m_PreviewManager.UpdateMasterPreview(ModificationScope.Node);
        }

        void OnMouseDragPreviewMesh(Vector2 deltaMouse)
        {
            Vector2 previewSize = m_PreviewTextureView.contentRect.size;

            m_PreviewScrollPosition -= deltaMouse * (Event.current.shift ? 3f : 1f) / Mathf.Min(previewSize.x, previewSize.y) * 140f;
            m_PreviewScrollPosition.y = Mathf.Clamp(m_PreviewScrollPosition.y, -90f, 90f);
            Quaternion previewRotation = Quaternion.Euler(m_PreviewScrollPosition.y, 0, 0) * Quaternion.Euler(0, m_PreviewScrollPosition.x, 0);
            m_Graph.previewData.rotation = previewRotation;
            //m_Graph.previewData.scale = m_Graph.previewData.scale;

            m_PreviewManager.UpdateMasterPreview(ModificationScope.Node);
        }

        public void Dispose()
        {
            m_PreviewManager.Dispose();
        }
    }

}