namespace Nmf.Client;

/// <summary>
/// Every key and click the game answers to, in one list (spec 2026-10-10-controls-design): the F1 help and the settings
/// window both show it. A key added to GameRoot.HandleKey goes into <see cref="HandledKeys"/> and here.
/// </summary>
public static class ControlsHelp
{
    /// <summary>The keys and modifiers the game reacts to; each must appear in the help (checked by a test).</summary>
    public static readonly string[] HandledKeys =
        ["Shift", "Alt", "Ctrl", "E ", "Space", "Z", "X", "C", "0", "1", "2", "P", "K", "L", "O", "H", "V", "+", "-", "Tab", "Esc", "B", "M", "Enter", "F1", "F11", "F ", "WASD", "R "];

    public static IReadOnlyList<(string Keys, string What)> Lines(string language) => language == "fi" ? Fi : En;

    private static readonly (string, string)[] En =
    [
        ("Nothing selected", "orders go to the whole platoon"),
        ("Click ground", "go there at their own pace · double click: run"),
        ("Alt/Option + click ground", "sneak there · Alt + double click: crawl"),
        ("Ctrl/Cmd + click", "area fire at that place (also: click a \"?\" mark)"),
        ("E held + click ground", "attack forward: go there, attacking every enemy seen on the way"),
        ("Click enemy", "fire at him"),
        ("Double click enemy or \"?\"", "attack his position: the men plan it (fire base, flank, assault on a suppressed position); with the plan preview on, the game stops and shows the plan first"),
        ("Plan shown: 1–4 · drag · Enter · R · Esc", "choose the tactic · drag the fire base, the form-up or the way (dragging the way adds a point, right click removes it) · start (or double click the target) · back to the suggestion · cancel"),
        ("Alt + double click enemy", "attack with long fire (from afar, nobody goes in)"),
        ("Ctrl/Cmd + double click enemy", "straight assault"),
        ("Double click fallen man", "the nearest man searches him (he carries at most twice his magazines and 4 grenades; the rest stays)"),
        ("Shift + any order", "add it to the queue: it starts when the one before is done; a halted attack stops the queue to ask"),
        ("Right button", "menu of the alternatives here: go, run, sneak, crawl, attack forward, area fire, take position, attack by tactic … each also + to the queue"),
        ("Click own soldier", "command only him (Shift adds) · double click: his whole squad"),
        ("Drag", "box select (Shift adds)"),
        ("Cards", "click selects, double click centres the camera; the squad name above them selects the squad"),
        ("1 / 2 · 0", "select the strike / support squad (again: look at it) · the whole platoon"),
        ("Tab / Esc", "select all / clear selection (Esc with nothing selected: the game menu)"),
        ("Z / X / C", "stand / crouch / go prone"),
        ("H", "halt"),
        ("P", "fire policy: fire at will / return fire / hold fire"),
        ("Space", "pause (orders still work)"),
        ("+ / -", "game speed ×0.25 … ×8"),
        ("B / M", "mission orders / map of the area"),
        ("V", "sounds as marks: heard shots, cracks and blasts drawn on the map"),
        ("K", "card size: large / small / icons"),
        ("L", "follow the men"),
        ("O", "clear outlines"),
        ("Hold the mouse still", "the cursor says what a click does; on an enemy or a mark: what our men know of him"),
        ("WASD, arrows, middle drag", "move the camera · wheel, pinch: zoom"),
        ("Enter", "close the mission's end panel"),
        ("F11", "full screen"),
        ("F (debug)", "reveal every unit"),
        ("F1", "this help"),
    ];

    private static readonly (string, string)[] Fi =
    [
        ("Ei valintaa", "käskyt koko joukkueelle"),
        ("Klikkaus maastoon", "mene sinne omaa tahtia · tuplaklikkaus: juokse"),
        ("Alt/Option + klikkaus maastoon", "hiivi sinne · Alt + tuplaklikkaus: ryömi"),
        ("Ctrl/Cmd + klikkaus", "aluetuli siihen kohtaan (myös: klikkaus \"?\"-merkkiin)"),
        ("E pohjassa + klikkaus maastoon", "hyökkää eteenpäin: mene sinne ja hyökkää jokaiseen matkalla nähtyyn viholliseen"),
        ("Klikkaus viholliseen", "ammu häntä"),
        ("Tuplaklikkaus viholliseen tai \"?\"", "hyökkää hänen asemaansa: miehet suunnittelevat (tuliasema, koukkaus, rynnäkkö lamautettuun asemaan); kun suunnitelman esikatselu on päällä, peli pysähtyy ja näyttää suunnitelman ensin"),
        ("Suunnitelma auki: 1–4 · veto · Enter · R · Esc", "valitse taktiikka · raahaa tuliasemaa, kokoontumispaikkaa tai reittiä (reitistä raahaus lisää pisteen, oikea klikkaus poistaa) · aloita (tai tuplaklikkaa kohdetta) · takaisin ehdotukseen · peru"),
        ("Alt + tuplaklikkaus viholliseen", "hyökkää pitkällä tulella (kaukaa, kukaan ei mene sisään)"),
        ("Ctrl/Cmd + tuplaklikkaus viholliseen", "suora rynnäkkö"),
        ("Tuplaklikkaus kaatuneeseen", "lähin mies tutkii hänet (kantaa enintään kaksinkertaiset lippaat ja 4 kranaattia; loput jäävät)"),
        ("Shift + mikä tahansa käsky", "lisää se jonoon: alkaa, kun edellinen on tehty; pysähtynyt hyökkäys pysäyttää jonon kysymään"),
        ("Oikea nappi", "valikko tämän kohdan vaihtoehdoista: mene, juokse, hiivi, ryömi, hyökkää eteenpäin, aluetuli, ota asema, hyökkää taktiikalla … jokainen myös + jonoon"),
        ("Klikkaus omaan mieheen", "komenna vain häntä (Shift lisää) · tuplaklikkaus: hänen ryhmänsä"),
        ("Vedä", "laatikkovalinta (Shift lisää)"),
        ("Kortit", "klikkaus valitsee, tuplaklikkaus keskittää kameran; ryhmän nimi niiden yllä valitsee ryhmän"),
        ("1 / 2 · 0", "isku- / tukiryhmä (uudelleen: katso sitä) · koko joukkue"),
        ("Tab / Esc", "valitse kaikki / tyhjennä valinta (Esc ilman valintaa: pelin valikko)"),
        ("Z / X / C", "seiso / kyykky / maahan"),
        ("H", "pysähdy"),
        ("P", "tulitoiminta: vapaa tuli / vastatuli / tulenavauskielto"),
        ("Space (välilyönti)", "tauko (käskyjä voi antaa)"),
        ("+ / -", "pelinopeus ×0,25 … ×8"),
        ("B / M", "tehtäväkäsky / alueen kartta"),
        ("V", "äänet kuvina: kuullut laukaukset, sivallukset ja räjähdykset kartalla"),
        ("K", "korttien koko: isot / pienet / ikonit"),
        ("L", "seuraa miehiä"),
        ("O", "selkeät reunat"),
        ("Hiiri paikallaan", "kursori kertoo, mitä klikkaus tekee; vihollisen tai merkin päällä: mitä miehet tietävät hänestä"),
        ("WASD, nuolet, keskinapin veto", "kamera · rulla, nipistys: zoomaus"),
        ("Enter", "sulje tehtävän loppupaneeli"),
        ("F11", "koko näyttö"),
        ("F (testaus)", "näytä kaikki yksiköt"),
        ("F1", "tämä ohje"),
    ];
}
