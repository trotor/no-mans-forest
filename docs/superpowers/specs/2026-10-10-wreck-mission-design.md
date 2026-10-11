# Hylkytehtävä (vaihe 8f, skenaarioiden eepos)

Eepos 3 (spec 2026-10-09-roadmap-epics) vaihe 6: hylkytehtävän kartta, joukot, käskynjako ja tasapaino. Tehtävä
kokoaa yhteen vaiheiden 8a–8e osat: tapahtumat, kantamisen, vihollisen valppauden, talven ja oman kartan.

Käyttäjä 10.10.2026: "tee päätöksiä ja ala toteuttaa".

## 1. Tilanne

Tammikuu 1943, Poventsan itäpuoli Vienan kanavan takana. Suomalainen tiedustelukone putosi aamulla vihollisen
selustaan. Lentäjä, luutnantti Saarinen, on elossa mutta ei pysty liikkumaan, ja hänellä on karttalaukku. Hiihtopartio
lähtee hakemaan häntä iltahämärissä.

- **Kartta:** `povenets`, uusi mapgen-alue (62,880 N, 34,960 E, 1 km × 1 km). Pohjoisessa on järvi, maasto on metsää,
  aukeita ja kumpareita, ja teitä ei ole. Vihollisen vartio on järven etelärannan kumpareella (34 m).
- **Olosuhteet:**
  - lunta 50 cm
  - hämärä (`light: dusk`)
  - alkuhetki 1943-01-27 15:40
- **Hylky:** kone teki pakkolaskun itäisen aukean (114 m) poikki lännestä ja pysähtyi sen itäpäähän (935, 615). Sen
  takana näkyy aura-ura lumessa. Hakualue on 120 m × 120 m, ja kone on 47 m sen keskeltä. Opasnuoli osoittaa alueen
  keskelle, eikä se siis vie koneelle.
- **Kone näkyy kartalla** (`props: plane_wreck`), kun joku omista näkee paikan, ja jää näkyviin sen jälkeen.

## 2. Joukot

**Pelaaja: Kers. Lehdon hiihtopartio.** Kaikki 6 miestä ovat suksilla:
- johtajalla on konepistooli
- kolmella kivääri
- yhdellä Lahti-Saloranta
- Sotm. Aholla on ahkio

Lisäksi on Luutn. Saarinen hylyllä: eksynyt, taistelukyvytön, kantaa karttalaukkua.

**Vihollinen** on rauhallinen ja sillä on 2 valorakettia:
- **Vartio, 4 miestä**, kumpareella. Puhelin on vartiossa, ja johto kulkee itään kartan reunaan.
- **Etsintäpartio, 4 miestä, suksilla:** lähtee kaakkoisnurkasta 3 minuutin kuluttua hylyn alueelle.
- **Reservi, 5 miestä:** kartan koillisnurkassa. Se lähtee, kun vartion puhelu menee perille.

## 3. Tavoitteet ja tapahtumat

**Tavoitteet:**
1. **Etsi hylky** (find).
2. **Tuo lentäjä takaisin** lähtöalueelle (rescue).
3. **Tuo karttalaukku** (piilotettu, paljastuu hylyllä).

**Tapahtumat:**
- **Hylky löytyy:** ilmoitus, ja karttalaukun tavoite paljastuu.
- **3 min:** etsintäpartio lähtee kaakkoisnurkasta (`search`), ja siitä tulee ilmoitus.
- **Puhelu menee perille:** 60 s:n kuluttua ilmoitus ja reservi lähtee hylylle (`send`). 90 s myöhemmin reservi
  siirtyy komentajalle (`release`).
- Lentäjän ja karttalaukun paluu päättää tehtävän samalla hetkellä, joten erillistä *turvassa*-ilmoitusta ei ole.
- **Vihollinen hylyn alueella:** ilmoitus *Vihollinen ehti hylylle*. Tehtävä ei pääty tähän, vaan siitä tulee
  taistelu hylystä.

Tehtävä epäonnistuu, jos lentäjä kuolee (rescue-tavoitteen sääntö).

## 4. Mittari

`probe-rescue` (Nmf.Cli) pelaa tehtävän kuten tavallinen pelaaja ilman näyttöä:
1. Koko partio hiihtää omaa vauhtiaan hakualueen keskelle. Halutessa viimeisen osuuden voi hiipiä (`--sneak <m>`).
2. Jos hylky ei näy keskeltä, partio kiertää alueen neljännekset.
3. Kun hylky löytyy, joku menee lentäjän viereen. Ahkiomies tai lähin lamautumaton mies ottaa lentäjän, ja kaikki
   palaavat lähtöalueelle.
4. Miehet ampuvat itse vihollista, jonka näkevät.

Mittari raportoi seed kerrallaan:
- tuloksen ja ajan
- omat tappiot
- tapahtuiko puhelu
- sen, ehtikö etsintäpartio hylylle ennen pelaajaa

**Tasapainon tavoite** 24 seedillä:
- onnistumisia 40–80 % (tehtävä on vaikea mutta mahdollinen)
- puhelu menee perille vähintään joka neljännessä

## 5. Testit

- `CoreContentTests` tarkistaa jo jokaisen tehtävän paikat, alueet ja aloituksen.
- `MissionLoaderTests`: `hylky`-tehtävä latautuu talvella, hämärässä, ja siinä on puhelin.
- Python: `RealAreaTests` ei koske uutta aluetta. Povenetsin data on tallennettu, ja generointi on deterministinen
  (sama tulos kahdesti).
- `probe-rescue`: komento ja yksi seed testinä.

## 6. Mittaus ja säätö (10.10.2026)

`probe-rescue --mission hylky --seeds 24 --minutes 30`:

| Asetelma | Pelastettu | Epäonnistui / lyöty | Puhelu | Huomio |
|---|---|---|---|---|
| Ensimmäinen versio: etsintä 4 min 75 m:n päästä, lähtö lounaisnurkasta | 0/4 | kaikki | 4/4 | vihollinen hylyllä 4:38, pelaaja 10:45 |
| Etsintä 6 min kaakkoisnurkasta, lähtö etelästä | 8/8 | 0 | 0 | ei kohtaamista |
| Sama, etsintä 4–5 min | 12/12 | 0 | 0 | etsintäpartio näki vilauksen ja jäi paikalleen |
| Etsintä odottaa näkyvissä olevaa vihollista ja jatkaa jälkiä (sim-korjaus), 2 min | 9/24 | 15 lyöty | 23 | liian vaikea |
| **Valittu: 3 min** | **15/24 (62 %)** | 3 + 6 lyöty | 18/24 | vihollinen aina hylyllä ensin, taistelu hylystä |

**Tulkinta:**
- Hiihtopartio löytää hylyn noin 7 minuutissa.
- Etsintäpartio on paikalla puoli minuuttia aiemmin, joten hylystä taistellaan.
- Taistelun ääni hälyttää vartion, joka soittaa kolmessa ajossa neljästä.
- Tavoitteet (40–80 % onnistumisia, puhelu vähintään joka neljännessä) täyttyvät.

**Sim-linjaus:** etsintäpartio ei enää luovu etsinnästä pelkän vilauksen takia.
- Näkyvissä olevan vihollisen aikana partio ei saa uusia käskyjä, ja miehet taistelevat itse.
- Kun kontakti katoaa, se jatkaa etsintää ja talvella jälkien seuraamista.
- Partio luovutetaan komentajalle vasta tulen alla.

Ilman tätä komentaja ei koskaan ajanut takaa, koska kukaan ei ollut ampunut, eikä partio tehnyt mitään.

### Loppukatselmoinnin jälkeen (10.10.2026)

Hylky oli täsmälleen hakualueen keskellä, joten opasnuoli vei suoraan sille. Lisäksi mittari pysähtyi 5–15 m:n
päähän lentäjästä ja odotti lamautunutta ahkiomiestä. Nyt hylky on 47 m keskeltä, ja mittari kiertää alueen.
Uusi mittaus (24 seediä):

| Pelaaja | Pelastettu | Epäonnistui | Lyöty | Puhelu |
|---|---|---|---|---|
| Oma vauhti (valittu mittari) | **18/24 (75 %)** | 3 | 3 | 19/24 |
| Hiipii viimeiset 80 m | 0/24 | 5 | 19 | 8/24 |

**Hiipiminen hangessa on lähes varma tappio.** Hiipivä mies on jalan, koska sukset eivät auta hiipiessä (talvispec
§2). Puolen metrin lumessa hän kulkee noin 0,5 m/s, ja suksilla liikkuva etsintäpartio ehtii hänen kimppuunsa. Tämä
on pelitestin kysymys: pitäisikö hiihtäjän saada hiipiä suksilla, vai onko tämä oikea opetus. Päätös odottaa
käyttäjän testipeliä.

**Pito etsinnässä:** näkyvissä olevan vihollisen aikana etsintä ei anna uusia käskyjä, mutta jo annetut liikkeet
jatkuvat. Kun kontakti katoaa, etsintä jatkuu siitä, mihin se jäi.

### Pelitestin jälkeen (11.10.2026)

Käyttäjä pyysi konetta aukealle, johon se olisi voinut tehdä pakkolaskun, ja konetta näkyviin.
- Kone on nyt itäisen aukean itäpäässä.
- Mittari menee suoraan koneelle, kun joku näkee sen. Niin tekisi oikea pelaajakin, koska kone näkyy.

Mittaus (24 seediä, oma vauhti):

| | |
|---|---|
| Pelastettu | **13/24 (54 %)** |
| Epäonnistui | 3 |
| Lyöty | 8 |
| Puhelu | 21/24 |

Aukealla taistelu on kovempi kuin metsän reunassa, mutta tulos on yhä tavoitteen sisällä.
