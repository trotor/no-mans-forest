# Hyökkäyssuunnitelman esikatselu (vaihe 6b)

Taktiikkaeepoksen runko on spec 2026-10-10-tactics-design. Käyttäjä valitsi 9.10.2026 linjan "esikatselu ja
vahvistus": tuplaklikkaus viholliseen pysäyttää pelin ja näyttää ehdotetun suunnitelman. Pelaaja voi vaihtaa taktiikan
ja raahata tuliasemaa ja reitin pisteitä. Enter aloittaa ja Esc peruu.

## 1. Mikä avaa esikatselun

Vaihe 7a (spec 2026-10-10-controls-design) muutti näppäinkarttaa sen jälkeen, kun runko kirjoitettiin: Shift lisää nyt
aina jonoon. Rungon "Shift + tupla aloittaa ehdotuksen heti" ei siis enää päde. Uusi kartta:

| Ele | Asetus "Näytä hyökkäyssuunnitelma ensin" päällä (oletus) | Asetus pois |
|---|---|---|
| Tuplaklikkaus viholliseen tai "?"-merkkiin | esikatselu, ehdotettu taktiikka valittuna | hyökkäys heti (ehdotus) |
| Alt + tupla (pitkä tuli), Ctrl + tupla (suora rynnäkkö) | heti: pelaaja on jo valinnut taktiikan | heti |
| Shift + tupla | jonoon kuten 7a:ssa, ilman esikatselua | jonoon |
| Oikean napin valikko | uusi rivi *Suunnittele hyökkäys…* avaa esikatselun; taktiikkarivit alkavat heti | taktiikkarivit heti |
| E + klikkaus (hyökkää eteenpäin) | ei esikatselua: matkalla kohdatut hyökkäykset alkavat heti | sama |

Esikatselu kuuluu vain pelaajan suoraan antamaan hyökkäykseen. Jonon hyökkäykset suunnitellaan vasta, kun jono
ehtii niihin, joten niistä ei näytetä vanhaa kuvaa.

## 2. Esikatselu auki

- **Peli pysähtyy** samalla tavalla kuin tehtäväkäskyn tai kartan ollessa auki (`OverlayPause`, avain "plan").
  Kun esikatselu sulkeutuu, peli jatkuu siinä tilassa, jossa se oli.
- **Paneeli ruudun yläreunassa** (paperia kuten muutkin paneelit):
  - otsikko: *Hyökkäys: asema, 3 tunnettua vihollista*
  - taktiikkanapit: **1 Tuli ja liike · 2 Pitkä tuli · 3 Suora rynnäkkö**. Ehdotetun napissa on merkki
    *(ehdotus)*. Jos taktiikasta ei synny suunnitelmaa, nappi on harmaa ja sen vihje kertoo syyn: *liian vähän
    miehiä (vähintään 4)* tai *ei sopivaa paikkaa*.
  - perustelu, esimerkiksi *Tuliasema 62 m päässä (3 miestä). Iskuryhmä (4) kiertää vasemmalta kokoontumispaikkaan
    35 m päähän.* Pitkä tuli: *Kaikki tuliasemaan 90 m päähän. Kukaan ei mene sisään.* Suora rynnäkkö: *Kaikki
    rynnäköivät suoraan kohteeseen ilman tukea.*
  - varoitukset punaisella:
    - *Tuliasema ei näe kohdetta: tuli on arvailua.*
    - *Reitti näkyy viholliselle (n kohtaa).*
    - *Kokoontumispaikka näkyy viholliselle.*
    - *Vihollisia tiedetään yhtä monta kuin meitä.*
    - *Sinne ei pääse.* (viimeisin raahaus ei kelvannut, ja vanha suunnitelma jäi voimaan)
  - napit: **Aloita (Enter) · Palauta ehdotus (R) · Peru (Esc)**.
- **Näppäimet:** 1–3 valitsevat taktiikan, Enter aloittaa, R palauttaa ehdotuksen ilman muokkauksia ja Esc peruu.
  Myös tuplaklikkaus samaan kohteeseen aloittaa. Kameran liike ja zoomaus toimivat, mutta muut näppäimet ja klikkaukset
  eivät anna käskyjä esikatselun aikana.

## 3. Kartalla

Suunnitelma piirretään kartalle samoilla väreillä kuin muut käskyt:

- **kohde:** rengas tähtäyspisteen ympärillä ja pienet renkaat aseman tunnetuille miehille
- **tuliasema:** jokaisen tuliryhmän miehen paikka pienenä ympyränä; tulilinja katkoviivana tuliaseman keskeltä kohteeseen;
  tuliryhmän reitti ohuena viivana (jos sillä on oma reittinsä)
- **iskuryhmän reitti:** nuoli kokoontumispaikkaan. Pätkät, jotka vihollinen näkee, ovat punaisia, ja muut vihreitä.
- **kokoontumispaikka:** iskumiesten paikat pieninä ympyröinä ja rynnäkkönuoli katkoviivana kohteeseen
- **suora rynnäkkö:** nuoli jokaiselta mieheltä kohteeseen.

**Kahvat** ovat isompia onttoja ympyröitä, jotka korostuvat hiiren alla:

- tuliaseman keskus (tuli ja liike, pitkä tuli)
- kokoontumispaikka (tuli ja liike)
- reitin välipisteet. Reitin viivasta raahaaminen lisää uuden välipisteen (enintään 3). Oikea klikkaus välipisteeseen
  poistaa sen.

Raahaus piirtää kahvaa hiiren mukana. Kun nappi vapautetaan, suunnittelija täydentää loput: miesten paikat kahvan
ympärille ja reitin palat välipisteiden kautta.

## 4. Suunnittelija ja muokkaukset (Nmf.Sim)

`TacticPlanner.Propose(sim, side, men, target, tactic, PlanEdits? edits)`.
`PlanEdits(Vec2? FireBase, Vec2? FormUp, IReadOnlyList<Vec2> Via)`:

- **FireBase:** tuliaseman paikat haetaan raahatun pisteen läheltä (12 m) samalla pisteytyksellä kuin ennen (näkyvyys,
  suoja, altistus). Taktiikan etäisyysvyö ja "omalla puolella" -ehto eivät rajaa: pelaaja päättää.
  - Ankkuri on lähin näkevä solu.
  - Jos lähellä ei ole yhtään kohteen näkevää solua, ankkuri on pistettä lähin kuljettava ja saavutettava solu, ja
    suunnitelma on sokea (`FireBaseBlind`).
- **FormUp:** kokoontumispaikka haetaan raahatun pisteen läheltä (6 m) kuljettavista ja saavutettavista soluista.
  Kulma- ja "näkymätön"-ehdot eivät rajaa, mutta näkyvyys merkitään (`RouteExposed`, reitin viimeinen piste on
  kokoontumispaikka). Kun FormUp on annettu ilman FireBasea, tuliasema lasketaan kuten ennen.
- **Via:** iskuryhmän reitti kulkee välipisteiden kautta järjestyksessä. Jokainen pala on oma halvin reittinsä
  (sama hinta: altistus ja oma tulilinja).
- **Ei mahdollista:** jos raahattuun kohtaan ei pääse, `Propose` palauttaa null. Esikatselu pitää silloin vanhan
  suunnitelman ja näyttää *Sinne ei pääse*.
- **Muokkaamaton suunnitelma** on täsmälleen sama kuin ennen (`edits` null tai tyhjä).
- **Determinismi ei muutu:** muokattu suunnitelma kulkee käskyssä kuten ennenkin. Jos kohde siirtyy ja simulaatio
  suunnittelee hyökkäyksen uudelleen (6a-2 §6), muokkaukset eivät ole mukana. Pelaajan raahaama paikka koski vanhaa
  asemaa.

## 5. Käynnissä olevat hyökkäykset

- Oman puolen käynnissä olevien hyökkäysten suunnitelmat piirretään himmeinä (läpinäkyvyys noin 35 %) ilman kahvoja,
  kunnes hyökkäys päättyy.
- Korteissa näkyy miehen osa ja vaihe: *Tuli: matkalla / lamauttaa* tai *Isku: matkalla / odottaa / rynnäkkö* (lyhyet, jotta ne mahtuvat pieneen korttiin).
  Teksti on `UnitStatus`-tyylinen ja näkyy suurissa ja pienissä korteissa.

## 6. Testaus

- `TacticPlannerTests` (muokkaukset):
  - tuliasema raahattuun kohtaan
  - kokoontumispaikka raahattuun kohtaan
  - reitti välipisteen kautta
  - paikka, johon ei pääse, palauttaa null
  - raahattu tuliasema, joka ei näe kohdetta, on sokea
  - tyhjät muokkaukset antavat saman suunnitelman kuin ilman niitä
- `AttackPreviewTests` (Nmf.Client):
  - avaus valitsee ehdotuksen
  - napit kertovat mahdottoman taktiikan syyn
  - taktiikan vaihto
  - raahaus suunnittelee uudelleen, ja huono raahaus pitää vanhan suunnitelman
  - välipisteen lisäys ja poisto
  - palautus
  - aloitus antaa `PlannedAttackOrder`in muokatulla suunnitelmalla, suora rynnäkkö `AssaultOrder`in
  - peruminen ei anna käskyä
  - perustelu ja varoitukset kummallakin kielellä
- `GameSessionTests`:
  - tuplaklikkaus avaa esikatselun asetuksen ollessa päällä, ja muuten hyökkäys alkaa heti
  - Shift + tupla menee jonoon
  - Alt + tupla ja Ctrl + tupla alkavat heti
- `ClickPreviewTests`: tuplaklikkauksen teksti on *tupla: suunnittele hyökkäys*. `ContextMenuTests`: rivi *Suunnittele
  hyökkäys…*.
- `ControlsHelpTests`: R ja esikatselun rivi ovat ohjeessa.
- `PlanPhaseTextTests`: korttien osa- ja vaihetekstit.
- Käsin: esikatselu, raahaus ja himmeät nuolet testikentällä kuvakaappauksin.
