using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FfxivImeBridge.NativeWrite;

/// <summary>
/// The ChatLog addon's node tree as text, one line per node, for finding the
/// game's input-mode badge (ticket 14): id, type, screen box, visibility and
/// what may drive it, then per type — a text node's string, font, colours and
/// edge/glow flags; an image or nine-grid node's part and the texture behind
/// it. Walks from the addon root through every component's own tree, so the
/// input component and its siblings are all there; the input's own node and
/// the channel label are marked. Reads node memory only. Main thread.
/// </summary>
internal static unsafe class ChatBoxNodeDump
{
    private const string ChatLogAddon = "ChatLog";
    /// <summary>The addon has a few hundred nodes; past this something is cyclic or not the ChatLog.</summary>
    private const int MaxLines = 800;
    /// <summary>
    /// A component node's <c>Type</c> is 1000 plus its index in the ULD's component
    /// list (<c>1012</c> for the ChatLog's text input), not <see cref="NodeType.Component"/>
    /// (10000) — the first dump matched the enum and expanded nothing.
    /// </summary>
    private const ushort ComponentTypeBase = 1000;

    private static bool IsComponent(AtkResNode* node) => (ushort)node->Type >= ComponentTypeBase;

    /// <summary>The dump, or one line saying why there is none.</summary>
    public static IReadOnlyList<string> Dump(IGameGui gui)
    {
        var addon = gui.GetAddonByName<AddonChatLog>(ChatLogAddon);
        if (addon == null) return ["ChatLog addon not loaded."];

        var input = addon->TextInput;
        var marks = new Dictionary<nint, string>();
        if (input != null && input->OwnerNode != null) marks[(nint)input->OwnerNode] = "<< text input component";
        if (input != null && input->AtkTextNode != null) marks[(nint)input->AtkTextNode] = "<< text input's text node";
        if (input != null && input->CursorContainer != null) marks[(nint)input->CursorContainer] = "<< text input's cursor node";
        if (addon->CurrentChannelTextNode != null) marks[(nint)addon->CurrentChannelTextNode] = "<< channel label";

        var lines = new List<string>
        {
            $"ChatLog addon at ({addon->AtkUnitBase.X},{addon->AtkUnitBase.Y}) scale={addon->AtkUnitBase.Scale:0.###} visible={addon->AtkUnitBase.IsVisible}; " +
            $"text input owner node id={(input == null || input->OwnerNode == null ? "-" : input->OwnerNode->NodeId.ToString())}, " +
            $"channel label node id={(addon->CurrentChannelTextNode == null ? "-" : addon->CurrentChannelTextNode->NodeId.ToString())}",
        };
        var seen = new HashSet<nint>();
        Walk(addon->RootNode, 0, lines, marks, seen);
        if (lines.Count >= MaxLines) lines.Add($"… stopped at {MaxLines} lines.");
        return lines;
    }

    /// <summary>Depth-first: the node, a component's own top-level nodes, then its children (<c>ChildNode</c> and its siblings).</summary>
    private static void Walk(AtkResNode* node, int depth, List<string> lines, Dictionary<nint, string> marks, HashSet<nint> seen)
    {
        if (node == null || lines.Count >= MaxLines || !seen.Add((nint)node)) return;
        lines.Add(Describe(node, depth, marks));

        if (IsComponent(node))
        {
            // A component's RootNode heads a sibling chain of its top-level nodes (the second dump showed one of
            // sixteen); both directions are walked and the visited set keeps each node to one line.
            var component = ((AtkComponentNode*)node)->Component;
            if (component != null) WalkSiblings(component->UldManager.RootNode, depth + 1, lines, marks, seen);
        }
        WalkSiblings(node->ChildNode, depth + 1, lines, marks, seen);
    }

    private static void WalkSiblings(AtkResNode* first, int depth, List<string> lines, Dictionary<nint, string> marks, HashSet<nint> seen)
    {
        for (var n = first; n != null; n = n->PrevSiblingNode) Walk(n, depth, lines, marks, seen);
        if (first != null) for (var n = first->NextSiblingNode; n != null; n = n->NextSiblingNode) Walk(n, depth, lines, marks, seen);
    }

    private static string Describe(AtkResNode* node, int depth, Dictionary<nint, string> marks)
    {
        var sb = new StringBuilder();
        sb.Append(' ', depth * 2);
        sb.Append($"#{node->NodeId} {(IsComponent(node) ? $"component[{(ushort)node->Type - ComponentTypeBase}]" : node->Type.ToString().ToLowerInvariant())} at ({node->ScreenX:0.#},{node->ScreenY:0.#}) {node->Width * node->GetScaleX():0.#}×{node->Height * node->GetScaleY():0.#}");
        sb.Append($" visible={(node->IsVisible() ? "yes" : "no")} alpha={node->Color.A} flags={node->NodeFlags}");
        if (node->Timeline != null) sb.Append($" timeline(label={node->Timeline->ActiveLabelId} frame={node->Timeline->FrameTime:0.#})");

        if (IsComponent(node))
        {
            var component = ((AtkComponentNode*)node)->Component;
            if (component == null) sb.Append(" component=null");
            else sb.Append($" component={component->GetComponentType()} nodes={component->UldManager.NodeListCount}");
        }
        else switch (node->Type)
        {
            case NodeType.Text:
                var text = (AtkTextNode*)node;
                sb.Append($" text=\"{text->NodeText}\" font={text->FontType}/{text->FontSize}pt align={text->AlignmentFontType} colour=#{text->TextColor.RGBA:X8} edge=#{text->EdgeColor.RGBA:X8} background=#{text->BackgroundColor.RGBA:X8} textFlags={text->TextFlags} lineSpacing={text->LineSpacing}");
                break;
            case NodeType.Image:
                var image = (AtkImageNode*)node;
                sb.Append($" part={image->PartId} wrap={image->WrapMode} imageFlags={image->Flags} {DescribePart(image->PartsList, image->PartId)}");
                break;
            case NodeType.NineGrid:
                var grid = (AtkNineGridNode*)node;
                sb.Append($" part={grid->PartId} offsets(t/b/l/r)={grid->TopOffset}/{grid->BottomOffset}/{grid->LeftOffset}/{grid->RightOffset} blend={grid->BlendMode} {DescribePart(grid->PartsList, grid->PartId)}");
                break;
        }

        if (marks.TryGetValue((nint)node, out var mark)) sb.Append(' ').Append(mark);
        return sb.ToString();
    }

    /// <summary>The part's texture rectangle and the file it comes from, as far as the pointers go.</summary>
    private static string DescribePart(AtkUldPartsList* list, uint partId)
    {
        if (list == null) return "parts=null";
        if (partId >= list->PartCount) return $"parts={list->PartCount} (part {partId} out of range)";
        var part = list->Parts[partId];
        var sb = new StringBuilder($"uv=({part.U},{part.V}) {part.Width}×{part.Height}");
        var asset = part.UldAsset;
        if (asset == null) return sb.Append(" asset=null").ToString();
        sb.Append($" asset={asset->Id}");
        var texture = &asset->AtkTexture;
        sb.Append($" textureType={texture->TextureType}");
        if (texture->TextureType == TextureType.Resource && texture->Resource != null)
        {
            var resource = texture->Resource;
            sb.Append($" icon={resource->IconId}");
            var handle = resource->TexFileResourceHandle;
            if (handle != null) sb.Append($" path=\"{Encoding.UTF8.GetString(handle->ResourceHandle.FileName)}\"");
        }
        return sb.ToString();
    }
}
