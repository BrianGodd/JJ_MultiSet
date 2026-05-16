using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal class PanelController
{
    private readonly EditorManager editorManager;

    public PanelController(EditorManager editorManager)
    {
        this.editorManager = editorManager;
    }

    public void InitializePanels()
    {
        if (editorManager.labelInputPanel != null) editorManager.labelInputPanel.SetActive(false);
        if (editorManager.settingPanel != null) editorManager.settingPanel.SetActive(false);
        if (editorManager.scriptingPanel != null) editorManager.scriptingPanel.SetActive(false);
        if (editorManager.simulationPanel != null) editorManager.simulationPanel.SetActive(false);
    }

    public void OnApplyUpload()
    {
        string title = editorManager.uploadTitleInput != null ? editorManager.uploadTitleInput.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(title))
        {
            Debug.LogError("Upload title cannot be empty.");
            return;
        }
        editorManager.uploadDB.UploadMark(title);
    }

    public void OnBackToMenu()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ShowUploadInfo()
    {
        if (editorManager.marksInfoPanel == null) return;

        foreach (var mark in editorManager.createdMarkers)
        {
            if (mark != null) mark.SetActive(false);
        }

        var containerGO = editorManager.marksInfoPanel.gameObject;
        var vlg = containerGO.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = containerGO.AddComponent<VerticalLayoutGroup>();
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(4, 4, 4, 4);

        var csf = containerGO.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = containerGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        for (int i = editorManager.marksInfoPanel.childCount - 1; i >= 0; i--)
        {
            Object.Destroy(editorManager.marksInfoPanel.GetChild(i).gameObject);
        }

        if (editorManager.marksInfo != null) editorManager.marksInfo.text = string.Empty;

        foreach (var mark in editorManager.createdMarkers)
        {
            if (mark == null) continue;
            string label = mark.name;

            var btnGO = new GameObject("MarkButton_" + label, typeof(RectTransform));
            btnGO.transform.SetParent(editorManager.marksInfoPanel, false);

            var img = btnGO.AddComponent<Image>();
            img.color = Color.white;
            var btn = btnGO.AddComponent<Button>();

            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.minHeight = 28f;
            le.flexibleWidth = 1f;

            var txtGO = new GameObject("Text", typeof(RectTransform));
            txtGO.transform.SetParent(btnGO.transform, false);
            var tmp = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.black;
            tmp.fontSize = 18;

            var rtTxt = txtGO.GetComponent<RectTransform>();
            rtTxt.anchorMin = new Vector2(0f, 0f);
            rtTxt.anchorMax = new Vector2(1f, 1f);
            rtTxt.offsetMin = new Vector2(10f, 0f);
            rtTxt.offsetMax = new Vector2(-10f, 0f);

            string detailsText = MarkStorage.TryGet(label, out var md)
                ? $"Label: {label}\nposition=({md.position.x:F2},{md.position.y:F2},{md.position.z:F2})\nscale=({md.scale.x:F2},{md.scale.y:F2},{md.scale.z:F2})\nmargin={md.margin:F2}\nangle1={md.angle1:F1}\nangle2={md.angle2:F1}\nkeyword={md.keyword}\ndetails={md.details}"
                : $"Label: {label}\n(no saved data)";

            string title = label;
            string payload = detailsText;

            btn.onClick.AddListener(() =>
            {
                var panelGO = new GameObject("MarkDetailPanel_" + title, typeof(RectTransform));
                var parentForPanel = editorManager.uploadPanel != null ? editorManager.uploadPanel.transform : editorManager.marksInfoPanel.transform;
                panelGO.transform.SetParent(parentForPanel, false);

                var panelImg = panelGO.AddComponent<Image>();
                panelImg.color = new Color(1f, 1f, 1f, 0.95f);
                var panelBtn = panelGO.AddComponent<Button>();

                var prt = panelGO.GetComponent<RectTransform>();
                prt.anchorMin = new Vector2(0.5f, 0.5f);
                prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = Vector2.zero;
                prt.sizeDelta = new Vector2(360f, 200f);
                prt.localPosition = new Vector3(200f, 0f, 0f);

                var dtxtGO = new GameObject("DetailText", typeof(RectTransform));
                dtxtGO.transform.SetParent(panelGO.transform, false);
                var dtmp = dtxtGO.AddComponent<TextMeshProUGUI>();
                dtmp.text = payload;
                dtmp.alignment = TextAlignmentOptions.TopLeft;
                dtmp.color = Color.black;
                dtmp.fontSize = 16;

                var drt = dtxtGO.GetComponent<RectTransform>();
                drt.anchorMin = new Vector2(0f, 0f);
                drt.anchorMax = new Vector2(1f, 1f);
                drt.offsetMin = new Vector2(8f, 8f);
                drt.offsetMax = new Vector2(-8f, -8f);

                panelBtn.onClick.AddListener(() => Object.Destroy(panelGO));
            });
        }
    }

    public void ActiveMode(int mode)
    {
        ResetModeUI();
        if (editorManager.nextButton != null) editorManager.nextButton.SetActive(mode <= 4);

        switch (mode)
        {
            case 0:
                if (editorManager.menuPanel != null) editorManager.menuPanel.gameObject.SetActive(true);
                break;
            case 1:
                if (editorManager.editorPanel != null) editorManager.editorPanel.gameObject.SetActive(true);
                if (editorManager.drawPanel != null) editorManager.drawPanel.gameObject.SetActive(true);
                if (editorManager.Mask1 != null) editorManager.Mask1.SetActive(false);
                break;
            case 2:
                if (editorManager.editorPanel != null) editorManager.editorPanel.gameObject.SetActive(true);
                if (editorManager.drawPanel != null) editorManager.drawPanel.gameObject.SetActive(true);
                if (editorManager.Mask2 != null) editorManager.Mask2.SetActive(false);
                if (editorManager.ModeButton2 != null) editorManager.ModeButton2.interactable = true;
                break;
            case 3:
                if (editorManager.editorPanel != null) editorManager.editorPanel.gameObject.SetActive(true);
                if (editorManager.drawPanel != null) editorManager.drawPanel.gameObject.SetActive(true);
                if (editorManager.Mask3 != null) editorManager.Mask3.SetActive(false);
                if (editorManager.ModeButton3 != null) editorManager.ModeButton3.interactable = true;
                break;
            case 4:
                if (editorManager.editorPanel != null) editorManager.editorPanel.gameObject.SetActive(true);
                if (editorManager.drawPanel != null) editorManager.drawPanel.gameObject.SetActive(true);
                if (ToolManager.Instance != null) ToolManager.Instance.ResetRotation();
                break;
            case 5:
                if (editorManager.editorPanel != null) editorManager.editorPanel.gameObject.SetActive(false);
                if (editorManager.drawPanel != null) editorManager.drawPanel.gameObject.SetActive(true);
                if (editorManager.uploadPanel != null) editorManager.uploadPanel.SetActive(true);
                ShowUploadInfo();
                break;
        }
    }

    private void ResetModeUI()
    {
        if (editorManager.menuPanel != null) editorManager.menuPanel.gameObject.SetActive(false);
        if (editorManager.editorPanel != null) editorManager.editorPanel.gameObject.SetActive(false);
        if (editorManager.drawPanel != null) editorManager.drawPanel.gameObject.SetActive(false);
        if (editorManager.uploadPanel != null) editorManager.uploadPanel.SetActive(false);

        if (editorManager.labelInputPanel != null) editorManager.labelInputPanel.SetActive(false);
        if (editorManager.settingPanel != null) editorManager.settingPanel.SetActive(false);
        if (editorManager.scriptingPanel != null) editorManager.scriptingPanel.SetActive(false);
        if (editorManager.simulationPanel != null) editorManager.simulationPanel.SetActive(false);
        if (editorManager.selectionRect != null) editorManager.selectionRect.gameObject.SetActive(false);

        if (editorManager.Mask1 != null) editorManager.Mask1.SetActive(true);
        if (editorManager.Mask2 != null) editorManager.Mask2.SetActive(true);
        if (editorManager.Mask3 != null) editorManager.Mask3.SetActive(true);

        foreach (var mark in editorManager.createdMarkers)
        {
            if (mark != null) mark.SetActive(true);
        }

        editorManager.SelectMarker(null);
    }
}
