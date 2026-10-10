# Taktiikat (eepos 2, vaiheet 6a–6c)

Käyttäjä sanoi 9.10.2026, että hyökkäykset näyttävät itsemurhatehtäviltä. Tuplaklikkauksesta miesten pitää etsiä paras
tapa hyökätä. Suunnitelman pitää olla pelaajan muokattavissa, ja sen pitää riippua tehtävästä ja miehistä. Tämä spec
kuvaa koko eepoksen rungon ja vaiheen 6a tarkasti. Vaiheet 6b ja 6c saavat omat tarkemmat specinsä.

## Käyttäjän linjaukset (9.–10.10.2026)

- **Esikatselu.** Tuplaklikkaus viholliseen pysäyttää pelin ja näyttää ehdotetun suunnitelman. Pelaaja voi vaihtaa
  taktiikan, raahata tuliasemaa ja reitin pisteitä ja aloittaa Enterillä. Esc peruu, ja Shift + tuplaklikkaus aloittaa
  ehdotuksen heti (vaihe 6b).
- **Ensimmäiset taktiikat:** tuli ja liike, soluttautuminen ja pitkä tuli. Pihtiliike tulee myöhemmin.
- **Kun suunnitelma menee pieleen,** miehet keskeyttävät hyökkäyksen itse. He hakeutuvat suojaan ja ampuvat takaisin,
  tuliasema jatkaa lamautusta, ja pelaaja saa ilmoituksen ja päättää jatkosta. Keskeytysraja riippuu miesten
  kokemuksesta ja hermosta, ja johtajan kaatuminen keskeyttää aina.
- **Vihollinen** käyttää samaa suunnittelijaa vastahyökkäyksissä (vaihe 6c).
- **Tehtävä** voi suosia tai kieltää taktiikoita (vaihe 6c).

## Nykytila ja sen heikkoudet

Tuplaklikkaus lähettää `AttackOrder`in, ja `AttackPlanner` vie sen läpi kahden puoliskon syöksyinä suoraan kohti
kohdetta (spec 2026-09-26-attack-design). Selvitys 9.10.2026 ja tasapainotarkistus (2026-09-26-balance-check-design)
löysivät nämä heikkoudet:

1. Tukiasemaa ja koukkausta ei ole. Hyökkäys alkaa siitä, missä miehet seisovat, usein jo 25–45 metrin päässä.
2. Suojaava mies ampuu vain, jos näkee kohteen itse. Alueammuntaa tulee vain, kun koko puoli on kadottanut kohteen.
3. Syöksyjät juoksevat pystyssä ampumatta eivätkä reagoi tuleen. Suojaa haetaan vain kohdetta vastaan ja vain
   8 metrin säteeltä.
4. Rynnäkkö lähtee 10 sekunnin odotuksen jälkeen, vaikka kohdetta ei olisi lamautettu.
5. Kun kohde kaatuu, hyökkäys jatkuu seuraavaan viholliseen syvemmälle asemaan.

Mittari antoi suoralle hyökkäykselle suomalaisia poissa 7,0/7. Kun tukiryhmä ampui aluetulta 70 metristä ja
iskuryhmä tuli sivusta, tulos oli 5,9/7 ja vihollisia poissa 4,9/9.

## Arkkitehtuuri

Ensin suunnitelma, sitten toteutus. Suunnittelija on puhdas funktio pelitilasta ja oman puolen tiedosta. Suunnitelma
kulkee käskyn mukana simulaatioon, ja simulaatio toteuttaa sen tilakoneena.

```
GameSession (tuplaklikkaus) ──► TacticPlanner.Suggest(sim, puoli, miehet, kohde, taktiikat) ──► TacticPlan
        │                                         (lukee vain: kartta, oma tieto, omat miehet)
        └─► Submit(PlannedAttackOrder(plan)) ──► Simulation: PlannedAttack + PlannedAttackRunner (tilakone)
```

- **Determinismi.** Suunnitelma on käskyssä, joten se kirjautuu käskylokiin ja uusinta toistaa sen. Suunnittelija käyttää
  vain kokonaislukuja ja kiinteää läpikäyntijärjestystä, eikä se arvo mitään. Se toimii samoin clientissä ja
  simulaatiossa, kun vihollinen käyttää sitä (6c).
- **Ei huijausta.** Suunnittelija näkee vihollisista vain sen, minkä puoli tietää (`SideKnowledge`: nähty, viimeksi
  nähty, epäilty).
- **Vanha hyökkäys** (`AttackOrder`, `AttackPlanner`) jää vihollisen vastahyökkäyksen käyttöön vaiheeseen 6c asti,
  ja 6c poistaa sen. Shift + tuplaklikkaus on edelleen suora rynnäkkö (`AssaultOrder`). Vaiheessa 6b se muuttuu
  ehdotuksen välittömäksi aloitukseksi.

## Taktiikat datana: `content/core/tactics/*.yaml`

```yaml
id: fire_and_movement
name: { en: "Fire and movement", fi: "Tuli ja liike" }
fire_base: { min_m: 35, max_m: 100 }          # tuliaseman etäisyys kohteesta
maneuver:                                     # puuttuu = koko ryhmä tuliasemaan (pitkä tuli)
  flank_deg: 60                               # rynnäkköpaikan kulma tuliaseman tulilinjasta
  form_up: { min_m: 25, max_m: 45 }           # rynnäkköpaikan etäisyys kohteesta
assault: { suppression: 250, wait_s: 20, give_up_s: 60 }   # puuttuu = ei rynnäkköä
abort: { casualties_pct: 30, pinned_s: 10 }
```

```yaml
id: long_fire
name: { en: "Long fire", fi: "Pitkä tuli" }
fire_base: { min_m: 50, max_m: 150 }
abort: { casualties_pct: 30, pinned_s: 10 }
```

`TacticLoader` (Nmf.Content) lukee tiedostot, ja tyypit ovat simulaation puolella (`Nmf.Sim.Tactics.TacticDef`), kuten
aseissa. Tarkistukset:
- `0 < min_m ≤ max_m ≤ 300`
- `flank_deg` 0–150
- `form_up.min_m ≤ form_up.max_m ≤ fire_base.max_m`
- `casualties_pct` 1–100
- `assault` vain, jos `maneuver` on annettu.

## Uhkakartta (`ThreatMap`)

Uhkakartta kertoo, mihin oman puolen tuntemat viholliset näkevät. Sitä käytetään tuliaseman, rynnäkköpaikan ja reitin
pisteytykseen.

- **Ruudukko.** 4 metrin ruudut ikkunassa kohteen ympärillä. Ikkunan säde on suurempi kuin `fire_base.max_m` ja
  suurempi kuin miesten keskipisteen etäisyys kohteesta, lisänä 40 m. Säde on enintään 250 m.
- **Lähteet.** Puolen kontaktit, joiden taso on Visible, LastKnown tai Suspected, kohde mukaan lukien. Paikka on
  kontaktin paikka, ei vihollisen todellinen paikka.
- **Altistus.** Ruutu on altis lähteelle, kun `LineOfSight.Clarity` vihollisen silmistä (maa + 100 cm) ruudun keskelle
  polvillaan olevan miehen korkeudelle (maa + 100 cm) on yli 0 ja etäisyys on alle 150 m. Ruudun arvo on niiden
  lähteiden määrä, joille se on altis.
- **Kustannus.** Lasketaan kerran suunnitelmaa tehtäessä. Tavoite on alle 50 ms karhumaki-kartalla ja 9 kontaktilla
  (mitataan testissä väljällä rajalla, `Timing`-kokoelma).

## Suunnittelija (`TacticPlanner`)

`TacticPlan? Propose(Simulation sim, Side side, IReadOnlyList<UnitId> men, UnitId target, TacticDef tactic)` palauttaa
suunnitelman tai null, jos taktiikka ei sovi. `Suggest` laskee ehdotuksen jokaiselle sallitulle taktiikalle ja valitsee
parhaan.

**Kohteen paikka** (`Aim`) on kohteen todellinen paikka, jos puoli näkee hänet, muuten kontaktin paikka. Jos kohdetta
ei tunneta, suunnitelmaa ei tehdä.

**Kohde on asema, ei yksi mies.** Kumpareen asemassa on viisi miestä poteroissa 10–20 metrin päässä toisistaan. Jos vain
klikattu mies lamautetaan, iskuryhmä juoksee lamauttamattoman pikakiväärin eteen. Siksi hyökkäyksen kohde on **asema**:
kaikki puolen tuntemat viholliset (nähty, viimeksi nähty, epäilty), jotka ovat enintään 30 metrin päässä `Aim`ista
suunnitelmaa tehtäessä. Asema päivitetään toteutuksen aikana: siihen tulee jokainen tunnettu vihollinen, joka on
enintään 30 metrin päässä aseman keskipisteestä.

**Ryhmät.**
- Jos miehiä on kahdesta ryhmästä tai useammasta, pikakivääriryhmä on tuliryhmä ja muut ovat iskuryhmä.
- Jos ryhmä on yksi, tuliryhmään tulevat pikakivääri ja parhaat ampujat (tarkkuuden mukaan), yhteensä puolet
  pyöristettynä ylöspäin. Muut, konepistoolimiehet ensin, ovat iskuryhmä.
- Pitkässä tulessa kaikki ovat tuliryhmää.
- Tuli ja liike tarvitsee vähintään neljä miestä. Muuten se ei sovi.

**Tuliasema.**
1. Ehdokkaat ovat 2 metrin välein kehällä `fire_base.min_m`–`max_m` kohteesta, enintään 70° sivuun suunnasta
   miesten keskipisteestä kohteeseen. Ehdokkaan pitää olla kulkukelpoinen ja samalla alueella kuin miehet
   (`Pathfinder.Reachable`).
2. Ehdokas kelpaa, jos kohde näkyy siitä: `Clarity` polvillaan ampuvan silmistä (maa + 100 cm) kohteen kohtaan (maa
   + 60 cm, poterossa olevalle reunan korkeus + 30 cm) on vähintään 60, ja etäisyys on enintään tuliryhmän lyhimmän
   aseen kantama.
3. Pisteet: `näkyvyys + suoja kohdetta vastaan (CoverFinder.CoveredAt) + 2 × etäisyys metreinä − 60 × altistus
   muille kuin kohteelle − matka miehiltä metreinä / 2`. Paras ehdokas on tuliaseman keskus.
4. Jokainen tuliryhmän mies saa oman paikkansa enintään 12 metrin päästä keskuksesta ja vähintään 3 metrin päästä
   toisistaan. Pikakivääri valitsee ensin.
5. Jos yhtään kelvollista ehdokasta ei ole edes `min_m`-rajalla, haku jatkuu 5 metrin askelin lähemmäs 25 metriin
   asti.
6. Jos ehdokasta ei löydy sittenkään, tuliasemana on tuliryhmän nykyinen paikka lähimpine suojineen
   (`CoverFinder`). Suunnitelmaan merkitään `FireBaseBlind`, ja se laskee pisteitä.

**Rynnäkköpaikka** (vain tuli ja liike).
1. Ehdokkaat ovat kehällä `form_up.min_m`–`max_m` kohteesta. Kulman tulilinjasta (tuliaseman keskus → kohde) pitää
   olla vähintään `flank_deg`, molemmille puolille.
2. Pisteet ilman reittihakua: `suoja kohdetta vastaan − 60 × altistus − suora etäisyys iskuryhmästä metreinä / 2`.
3. Viidelle parhaalle ehdokkaalle haetaan painotettu reitti (alla) rajatulla solmubudjetilla. Paras on se, jonka
   `pisteet − reitin altistushinta / 100` on suurin. Jos mikään reitti ei löydy, tuli ja liike ei sovi.

**Reitti.**
- A*-haku iskuryhmän keskipisteestä rynnäkköpaikkaan lisäkustannuksella:
  - jokainen solu altistusruudussa maksaa `altistus × 300`
  - solu, joka on alle 8 metrin päässä tulilinjasta (tuliaseman keskus → kohde) tuliaseman ja kohteen välissä,
    maksaa 2000, jotta oma tuli ei osu.
- `Pathfinder.FindPath` saa ylikuorman, jolla on solukohtainen lisäkustannus (`Func<int, int, int>`, solun x ja y).
  Nykyiset haut eivät muutu.
- Reitti tallennetaan suunnitelmaan pisteinä enintään 10 metrin välein.

**Oikealla kartalla.** Suunnittelija testataan myös karhumaki-kartalla Iskuosaston todellisilla kontaktipaikoilla:
suunnitelma syntyy, ja se valmistuu aikarajassa (väljä raja, `Timing`-kokoelma). Pienet testikartat eivät paljasta
solmubudjetin loppumista suurella kartalla.

**Taktiikan valinta** (`Suggest`).
- Pisteet ovat suunnitelman oma laatu: tuliaseman pisteet, ja tulessa ja liikkeessä lisäksi rynnäkköpaikan
  pisteet ja reitin altistus.
- Kertoimet:
  - tunnettujen vihollisten määrä verrattuna omiin taistelukykyisiin; jos tunnettuja on vähintään yhtä paljon,
    tuli ja liike saa −200
  - tuli ja liike saa −300, jos tuliasema on `FireBaseBlind`.
- Tasapisteissä voittaa taktiikoiden listajärjestys: tuli ja liike ennen pitkää tulta.
- Ehdotus sisältää lyhyen perustelun pelaajalle, esimerkiksi "Tuliasema 72 m, koukkaus vasemmalta".

`TacticPlan` on pelkkää dataa:
- `TacticId`, `Target`, `Aim`
- `FireTeam` (miehet ja paikat), `ManeuverTeam` (miehet)
- `FormUp` (paikka tai null), `Route` (pisteet)
- `FireBaseBlind`, `Score`
- `Reason` (avain ja luvut, jotka client kääntää).

## Toteutus (`PlannedAttack`, `PlannedAttackRunner`)

`PlannedAttackOrder(UnitId Unit, TacticPlan Plan)`: `Unit` on suunnitelman ensimmäinen mies, ja käsky koskee kaikkia
suunnitelman miehiä.
- Käsky hylätään, jos kohde on kaatunut tai kaikki miehet ovat maassa, kaatuneet tai murtuneet.
- Mies, joka ei voi osallistua (maassa, kaatunut, murtunut), jätetään pois.
- Jokainen mukaan otettu mies jättää aiemman käskynsä samoin kuin nykyisessä `AttackOrder`issa. Hänen
  `PlannedAttackId` asetetaan.

Hyökkäys etenee vaiheittain. Tila päivitetään aivojen tahdissa (5 askelta) samaan tapaan kuin `AttackPlanner`.
Tuliryhmä ja iskuryhmä lähtevät liikkeelle yhtä aikaa. Jos iskuryhmä odottaisi tuliaseman edessä, se olisi oman tulen
tiellä (`FriendInLine`, `FriendNearAreaFire`), koska metsässä kontakti syntyy usein 26 metrissä ja tuliasema on
35 metrin päässä tai kauempana.

| Vaihe | Tuliryhmä | Iskuryhmä | Seuraava vaihe, kun |
|---|---|---|---|
| `Deploy` | Siirtyy paikoilleen omalla tahdillaan (`AutoPace`). Pitkässä tulessa kaikki ovat tuliryhmää. | Lähtee heti reitille rynnäkköpaikkaan `AutoPace`-tahdilla (hiipii vihollisen lähellä, juoksee tulen alla). Jokaisella on oma paikkansa 3 metrin välein. | Tuliryhmä on paikoillaan ja ampuma-asennossa, tai 60 s on kulunut. |
| `Suppress` | Ampuu aseman miehiä. Pikakivääri ampuu vaarallisinta (nähty pikakivääri, muuten kohde), ja muut jakautuvat aseman nähtyjen miesten kesken lähimmästä alkaen. Jos mies ei itse näe kohdettaan, hän ampuu aluetulta tämän tunnettuun paikkaan, vaikka puoli näkisi tämän. Tuntemattomaan tai epäiltyyn aseman mieheen ammutaan aluetulta. Pitkä tuli jää tähän vaiheeseen. | Jatkaa reittiä ja odottaa rynnäkköpaikassa. | Tuliryhmä on ampunut vähintään 5 s, ja iskuryhmän kaikki pystyssä olevat miehet ovat rynnäkköpaikassa. |
| `Assault` | Jatkaa tulta. Aluetuli pysähtyy itsestään, kun omat ovat alle 5 metrin päässä paikasta tai tulilinjalla (`Firing.FriendNearAreaFire`). | Odottaa, kunnes jokainen aseman nähty mies on lamautettu (maassa, tai lamautus ≥ `assault.suppression` × kokemuskerroin) tai kaatunut. Jos `wait_s` on kulunut ja jokaisen nähdyn lamautus on vähintään puolet rajasta, se ei odota enempää. Sitten jokainen rynnäköi lähimpään aseman mieheen (`StartAssault`). | Aseman kaikki tunnetut miehet ovat poissa taistelusta (onnistui), tai `give_up_s` kuluu ilman rynnäkköä (keskeytys: "asema ei lamaudu"). |

**Tuli, joka ei ala.** Jos tuliryhmä ei ole ampunut yhtään laukausta 20 sekuntiin `Suppress`-vaiheen alusta,
hyökkäys keskeytyy syyllä "ei tulta". Silloin miehet eivät näe asemaa eivätkä pysty ampumaan sitä.

**Kohde liikkuu.** Toteutus ampuu ja rynnäköi aseman miesten nykyisiin tunnettuihin paikkoihin. Jos aseman keskipiste
siirtyy yli 20 metriä suunnitelman `Aim`ista, hyökkäys keskeytyy syyllä "kohde siirtyi". Esimerkiksi vihollisen
partio on lähtenyt liikkeelle, tai 6b:n esikatselussa suunnitelma tehtiin tauolla ja ehti vanhentua.

**Kun asema on vallattu,** hyökkäys päättyy, eikä se jatku 30 metriä kauemmas. Miehet jäävät paikoilleen
ampuma-asentoon, ja tavallinen käytös jatkuu. Tulee tapahtuma `PlannedAttackEnded(Tick, Side, Id, Success: true)`.

**Keskeytys** (`PlannedAttackHalted(Tick, Side, Id, Reason, Casualties)`). Hyökkäys keskeytyy, kun jokin näistä
toteutuu:
- `casualties` ≥ `max(1, alkuperäinen vahvuus × casualties_pct × (100 + keskimääräinen hermo − 50) / 100 / 100)`.
  Kaatuneet ja vähintään vakavasti haavoittuneet lasketaan.
- Hyökkäyksen johtaja kaatuu. Johtaja on miehistä se, jolla on `IsLeader` ja suurin johtajuus, tai ensimmäinen
  mies, jos johtajaa ei ole.
- Iskuryhmästä vähintään puolet on ollut maahan painettuna yhtäjaksoisesti `pinned_s` sekuntia.
- Asema ei lamaudu, tulta ei ala tai kohde siirtyi (yllä).

Keskeytyksen jälkeen:
- Iskuryhmä vapautuu tavalliseen käytökseen. Mies hakeutuu suojaan tulen alla (`ReactToFire`) ja ampuu takaisin.
- Tuliryhmän miehet jäävät aluetuleen kohteen paikkaan pysyvänä käskynä, kuten `AreaFireOrder`.

Mies jättää hyökkäyksen, kun hän saa toisen käskyn, kaatuu tai murtuu. Hyökkäys, jossa ei ole enää ketään, päättyy.

Iskuryhmän mies reagoi tuleen siirtymän aikana. Tulen alla `AutoPace` vaihtaa juoksuun, ja maahan painettu mies jää
maahan. Toisin kuin nykyisessä hyökkäyksessä, `ReactToFire`-suojautumista ei kuitenkaan tehdä siirtymän aikana,
koska reitti on jo valittu suojaisaksi.

**Tila ja hajautus.** `PlannedAttack`in kentät (vaihe, ajastimet, miehet, roolit, paikat, aseman miehet) ja miehen
`PlannedAttackId` lisätään `StateHash`iin. Uusinnat (`Replay`) pitävät käskyt muistissa olioina, joten
`PlannedAttackOrder` suunnitelmineen toistuu sellaisenaan. Tiedostoon tallentamista ei vielä ole.

## Client (6a)

- **Tuplaklikkaus** nähtyyn viholliseen kutsuu `TacticPlanner.Suggest` ja lähettää `PlannedAttackOrder`in. Jos
  ehdotusta ei synny, se lähettää `FireAtOrder`in ja kertoo syyn tilarivillä.
- **Ilmoitukset tilarivillä** (`Alerts`, fi ja en):
  - "Tuli ja liike: tuliasema 72 m, koukkaus vasemmalta"
  - "Isku lähtee"
  - "Asema vallattu"
  - "Hyökkäys pysähtyi: 2 haavoittunutta" (syy: tappiot, johtaja kaatui, iskuryhmä maassa, kohde ei lamaudu).
- Nuolia ja esikatselua ei vielä ole (6b).

## Mittari: `nmf probe-attack`

Tasapainotarkistuksen mittari tehdään uudelleen pysyväksi komennoksi (`Nmf.Cli`). Vanha mittari oli väliaikainen
tiedosto, joka on kadonnut.

```
dotnet run --project src/Nmf.Cli -- probe-attack --mission iskuosasto --tactic fire_and_movement --seeds 8 --minutes 8
```

- Suomalaiset lähestyvät tehtävän suunnitelmareittiä. Viimeiset 160 m he hiipivät, kuten pelaaja.
- Kun ensimmäinen vihollinen nähdään, tehdään hyökkäys:
  - `--tactic <id>`: suunnittelijan valinta pakotettuna
  - `--tactic suggest`: suunnittelijan oma valinta
  - `--tactic legacy`: vanha `AttackOrder`.
- Kohteeksi valitaan neuvostojohtaja, jos hänet nähdään, muuten lähin nähty vihollinen.
- Kun hyökkäys päättyy tai keskeytyy ja vihollisia nähdään vielä, uusi hyökkäys annetaan aikaisintaan 15 s kuluttua.
- Tulostus: siemenkohtaisesti ja keskiarvona suomalaisia poissa, vihollisia poissa, johtaja kaatui, käskyt saatu,
  keskeytykset syineen.

**Lähtötaso mitataan ensin.** Vanhat luvut (7,0/7 ja 5,9/7) tulivat kadonneesta mittarista, joten mittari tehdään
6a:n ensimmäisenä tehtävänä ja `--tactic legacy` -tulos kirjataan tähän specin loppuun.

**Tavoite vaiheelle 6a** (8 siementä, tuli ja liike verrattuna legacyyn):
- suomalaisia poissa keskimäärin vähintään 2 vähemmän
- vihollisia poissa vähintään yhtä monta
- neuvostojohtaja kaatuu useammin.

Tulos kirjataan specin loppuun. Jos tavoite jää saavuttamatta, syyt kirjataan, eikä lukuja viritetä sokeasti.

## Testaus (6a)

- `TacticLoaderTests`: kelvollinen tiedosto, jokainen tarkistus erikseen, ydinsisältö latautuu.
- `ThreatMapTests`:
  - avoimella kentällä ruutu on altis vihollisen nähdessä sen
  - kiven tai mäen takana ruutu ei ole altis
  - tuntematon vihollinen ei vaikuta
  - epäilty vaikuttaa kontaktin paikasta, ei todellisesta paikasta.
- `TacticPlannerTests` (pienet testikartat):
  - tuliasema valitaan näkyvyyden ja suojan mukaan
  - tuliasema ei ole kohteen takana
  - kun näkyvyyttä ei ole, haku jatkuu lähemmäs ja lopulta merkitään `FireBaseBlind`
  - rynnäkköpaikka on sivussa tulilinjasta
  - reitti kiertää vihollisen näkemän aukean, kun metsäkiertotie on olemassa
  - reitti ei kulje tulilinjalla
  - ryhmäjako kahdella ryhmällä ja yhdellä
  - tuli ja liike ei sovi kolmelle miehelle
  - `Suggest` valitsee pitkän tulen, kun tunnettuja vihollisia on enemmän
  - sama syöte antaa saman suunnitelman (determinismi).
- `PlannedAttackTests`:
  - vaiheet etenevät `Deploy` → `Suppress` → `Maneuver` → `Assault`
  - suojaava mies ampuu aluetulta, kun ei itse näe kohdetta
  - rynnäkkö ei lähde lamauttamattomaan kohteeseen ennen `wait_s`-aikaa
  - kaksi puolustajaa vierekkäisissä poteroissa: rynnäkkö odottaa, kunnes molemmat on lamautettu, ja tuliryhmä jakaa
    tulensa molempiin
  - metsäkontakti 26 metrissä: tuliryhmä vetäytyy tuliasemaan ja ampuu oikeasti, eikä iskuryhmä jää tulilinjalle
  - "ei tulta" -keskeytys, kun asema ei näy mistään
  - "kohde siirtyi" -keskeytys, kun aseman keskipiste siirtyy yli 20 m
  - jokainen keskeytyssyy erikseen, ja keskeytyksen jälkeen tuliryhmä on aluetulessa
  - aseman valtaus päättää hyökkäyksen, eikä yli 30 metrin päässä olevaan uuteen kohteeseen hyökätä
  - toinen käsky irrottaa miehen
  - uusinta toistaa hyökkäyksen (`StateHash` sama).
- `GameSessionTests`: tuplaklikkaus lähettää `PlannedAttackOrder`in. Ilman ehdotusta se lähettää `FireAtOrder`in ja
  syyn.
- `TacticPlannerTests` karhumaki-kartalla Iskuosaston kontaktipaikoista: suunnitelma syntyy aikarajassa.
- `probe-attack` ajetaan käsin, ja tulos kirjataan specin loppuun.

## Vaihe 6a-2: tappioiden lähteet (käyttäjän valinta 10.10.2026)

Mittauksen mukaan omat tappiot syntyvät siirtymissä ja keskeytysten jälkeen (ks. Mittaukset). Käyttäjä päätti, että
nämä korjataan samaan haaraan ennen mergeä.

1. **Poterot ovat mahdollisia ampujia.** Uhkakartta ottaa lähteiksi myös poterot (`CoverFinder.InPit`), jotka ovat
   enintään 30 metrin päässä kohteesta ja joissa ei jo ole tunnettua vihollista. Poterot näkyvät maastossa, joten
   tämä ei ole huijausta. Poterolähteen silmät ovat 150 cm poteron pohjasta, eli seisovan miehen pää reunan yllä.
2. **Rynnäkköpaikka on piilossa.** Rynnäkköpaikaksi kelpaa vain ruutu, jota mikään lähde ei näe. Jos kaistalla
   `form_up` sellaista ei ole, haku jatkuu 5 metrin askelin ulospäin 60 metriin asti. Jos piilossa olevaa paikkaa ei
   löydy, tuli ja liike ei sovi, ja ehdotus on pitkä tuli.
3. **Tuliryhmä kulkee suojassa.** Tuliryhmä saa oman reitin tuliasemaan samalla hintatulvalla kuin iskuryhmä (altistus
   maksaa, oma tulilinja ei). Reitti tallennetaan suunnitelmaan (`FireRoute`).
4. **Iskuryhmä ei ylitä aukeaa ennen tulta.** Reitin jokaiselle pisteelle merkitään, näkeekö jokin lähde sen
   (`RouteExposed`). `Deploy`-vaiheessa iskuryhmä etenee vain viimeiseen piilossa olevaan pisteeseen ennen
   ensimmäistä näkyvää. Loppumatka kuljetaan, kun tuliasema ampuu (`Suppress`).
5. **Keskeytyksen jälkeen iskuryhmä hakeutuu suojaan.** Mies, joka ei ole rynnäkössä, hakee lähimmän suojan aseman
   suuntaa vastaan (`CoverFinder.Find`) omalla tahdillaan. Tuliryhmä jää aluetuleen kuten ennen.
6. **Liikkuva kohde suunnitellaan uudelleen.** Kun aseman kaikki tunnetut miehet ovat siirtyneet yli 20 m (ennen:
   keskeytys "kohde siirtyi"), hyökkäys suunnitellaan uudelleen saman taktiikan mukaan samoilla miehillä. Kohteena on
   aseman lähin tunnettu mies, ja hyökkäys alkaa alusta (`Deploy`). Uudelleensuunnittelu tehdään simulaatiossa, joten
   se on deterministinen. Se tehdään enintään kolme kertaa saman hyökkäyksen aikana, minkä jälkeen keskeytyssyy on
   "kohde siirtyi". Jos uutta suunnitelmaa ei synny, syy on sama.

Tavoite pysyy samana kuin 6a:ssa.

## Myöhemmät vaiheet (runko)

**6b Suunnitelman esikatselu.**
- Tuplaklikkaus pysäyttää pelin ja piirtää suunnitelman kartalle: tuliaseman paikat, reitin nuolena, rynnäkköpaikan ja
  tulilinjan.
- Taktiikkanapit (1–3) ja perustelu.
- Raahattavat kahvat: tuliaseman keskus, reitin pisteet, rynnäkköpaikka. Raahauksen jälkeen suunnittelija täydentää
  loput, eli miesten paikat ja reitin palat.
- Enter aloittaa ja Esc peruu. Shift + tuplaklikkaus aloittaa ehdotuksen heti.
- Käynnissä olevan hyökkäyksen nuolet näkyvät kartalla himmeinä, ja vaihe näkyy korteissa.

**6c Soluttautuminen, tehtävät ja vihollinen.**
- Soluttautuminen: koko ryhmä hiipii reittiä, joka minimoi havaituksi tulemisen. Uhkakartta painottaa myös epäiltyjä ja
  kuuloetäisyyttä. Kranaatit heitetään ensin, ja tuliasemaa ei ole.
- Ehdotuksessa painavat miesten ominaisuudet: kokemus ja hiiviskely soluttautumisessa, ampujat tuliasemassa.
- `mission.yaml`: `tactics: { prefer: [...], forbid: [...] }`.
- Vihollisen `enemy_ai: { tactic: fire_and_movement }`: `EnemyCommander` tekee vastahyökkäyksen suunnittelijalla.
  Asemaan jäävät muodostavat tuliryhmän, ja reservi on iskuryhmä.
- Vanha `AttackOrder` ja `AttackPlanner` poistetaan.
- Tasapaino tarkistetaan mittarilla molempiin suuntiin.

## Mittaukset

**Lähtötaso (legacy, 10.10.2026).** `probe-attack --mission iskuosasto --tactic legacy --seeds 8 --minutes 8`:
suomalaisia poissa keskimäärin 5,0/7, vihollisia poissa 1,2/9, neuvostojohtaja kaatui 0/8 kertaa, hyökkäyskäskyjä
2,8 siementä kohden.

| Siemen | Suomalaisia poissa | Vihollisia poissa | Johtaja kaatui | Hyökkäyksiä |
|---|---|---|---|---|
| 1 | 5/7 | 1/9 | ei | 2 |
| 2 | 5/7 | 1/9 | ei | 3 |
| 3 | 6/7 | 1/9 | ei | 3 |
| 4 | 5/7 | 1/9 | ei | 2 |
| 5 | 7/7 | 1/9 | ei | 3 |
| 6 | 5/7 | 2/9 | ei | 3 |
| 7 | 4/7 | 1/9 | ei | 3 |
| 8 | 3/7 | 2/9 | ei | 3 |

Tavoite 6a:lle on siis: suomalaisia poissa enintään 3,0, vihollisia poissa vähintään 1,2 ja johtaja kaatuu useammin
kuin 0/8.

**Taktiikat 6a:n jälkeen (10.10.2026, katselmoinnin korjausten jälkeen).** Sama mittari, 8 siementä, 8 min:

| Taktiikka | Suomalaisia poissa | Vihollisia poissa | Johtaja kaatui | Hyökkäyksiä |
|---|---|---|---|---|
| legacy | 5,0 | 1,2 | 0/8 | 2,8 |
| tuli ja liike | 4,8 | 4,9 | 4/8 | 4,1 |
| pitkä tuli | 5,1 | 4,1 | 2/8 | 5,2 |
| suggest | 5,0 | 3,6 | 2/8 | 5,9 |

Tavoite toteutui osittain:
- Vihollisia poissa nousi 1,2:sta 4,9:ään, ja johtaja kaatuu 4/8 kertaa (ennen 0/8).
- Omat tappiot eivät juuri laskeneet (5,0 → 4,8), joten tavoite (enintään 3,0) jäi saavuttamatta.

Mistä omat tappiot tulevat (`--trace`, tuli ja liike, 8 siementä, 39 kaatunutta):
- 20 kaatui, kun hyökkäys ei ollut käynnissä: ennen ensimmäistä hyökkäystä, keskeytysten jälkeen tai hyökkäysten välillä.
- 13 kaatui `Deploy`-vaiheessa: 7 tuliryhmästä matkalla tuliasemaan ja 6 iskuryhmästä reitillä.
- 4 kaatui tulituessa (`Suppress`) ja 2 rynnäkössä (`Assault`).
- Keskeytykset: tappiot 6, johtaja kaatui 3, ei tulta 3, kohde siirtyi 3, iskuryhmä maassa 2, asema ei lamaudu 1.
  Asema vallattiin 6 kertaa.
- Testikentän demossa (`--demo=tactic`, ennen korjauksia) iskuryhmä juoksi kumpareen raivatun ampumasektorin yli
  rynnäkköpaikkaan ja kaatui minuutissa poteroiden miesten tuleen. Suunnittelija tunsi vain yhden kuullun miehen, joten
  aukea ei näkynyt sille vaarallisena. Lisäksi 25–45 metrin rynnäkkökaista osuu kokonaan 35–39 metrin raivioon.

Katselmointi löysi vikoja, jotka vääristivät ensimmäistä mittausta. Ne korjattiin ennen tätä mittausta:
- jo ennen käskyä haavoittunut mies laukaisi heti keskeytyksen "tappioita"
- aseman miehen kaatuminen laukaisi keskeytyksen "kohde siirtyi"
- jäljitys merkitsi keskeytyksen aiheuttaneen miehen "ei hyökkäyksessä" -mieheksi.

Lukuja ei viritetty. Jatkon vaihtoehdot ovat käyttäjän päätettävissä.

**6a-2:n jälkeen (10.10.2026), 24 siementä, 8 min.** Kahdeksan siemenen vaihtelu osoittautui suureksi, joten vertailu
tehtiin 24 siemenellä:

| Versio | Taktiikka | Suomalaisia poissa | Vihollisia poissa | Johtaja kaatui |
|---|---|---|---|---|
| legacy | – | 5,3 | 1,6 | 0/24 |
| 6a-2, säännöt 1–2 | tuli ja liike | 4,0 | 4,3 | 7/24 |
| 6a-2, säännöt 1–6 | tuli ja liike | 3,6 | 4,8 | 10/24 |
| 6a-2, säännöt 1–6 | suggest | 3,8 | 4,5 | 8/24 |

Tulos:
- Tulessa ja liikkeessä omat tappiot laskivat 5,3:sta 3,6:een (−1,7). Tavoite oli −2, joten se jäi niukasti
  saavuttamatta.
- Vihollisten tappiot kolminkertaistuivat, ja johtaja kaatuu 10/24 kertaa.

Mistä omat tappiot tulevat (sääntöjen 1–6 jälkeen, 16 siementä):
- noin puolet kaatuu hyökkäysten ulkopuolella
- noin kolmannes siirtymävaiheessa
- loput tulituessa.

Yleisimmät keskeytykset ovat tappiot ja "ei tulta": tuliasema ei näe poteroissa olevia miehiä. Tämä on 4s:n havainto
poteroihin kaivautuneesta vihollisesta, ja sen ratkaisu kuuluu tasapainovaiheeseen (aluetulen teho poteroihin).
