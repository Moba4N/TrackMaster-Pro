using TrackMaster.Core;

namespace TrackMaster.Web;

/// <summary>Register „Ausstattung“: eigene Modelle (Pro).</summary>
public partial class App
{
    /// <summary>Gruppen für eigene Modelle im Register „Ausstattung“.</summary>
    private IEnumerable<Group> EquipmentProGroups() => [];

    private void AddEquipmentProCommands() { }
}
