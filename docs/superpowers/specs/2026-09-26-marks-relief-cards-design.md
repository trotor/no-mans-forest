# Tutkitut ruumiit, maaston korkeudet, korttikoot, reunat ja testikenttä (vaihe 4q)

Käyttäjän toiveet 26.9.2026:
- Merkitse jo tutkitut kaatuneet eri tavalla.
- Korkeammat paikat erottumaan: grafiikka naiivimmaksi mutta luonnollisemmaksi.
- Omien korttien koko vaihdettavaksi: iso, pieni tai ikoni.
- Pikkukorttien symboliikka selkeämmäksi.
- Löytöteksti aina luettavan kokoiseksi.
- Joukoille tarvittaessa selkeämmät reunat.
- Oma testitehtävä debuggausta varten.

## Tutkitut ruumiit

- `Unit.WasSearchedBy(Side)`: simulaatio muistaa puolittain, kuka ruumiin on tutkinut, vaikka jotain jäisi.
  - Tieto on StateHashissa.
- `BodyMarks.Of(unit, pelaaja)` palauttaa None, Unsearched tai Searched.
  - Vain omien tutkimat merkitään.
  - Vihollisen tutkimisesta pelaaja ei tiedä.
- Merkinnät:
  - Lähellä tutkitun ruumiin reppu vaihtuu vihreään ✓-merkkiin, jonka koko seuraa zoomia.
  - Kaukana ja M-kartalla risti haalistuu, ja viereen tulee ✓.
- Vihje kertoo "Tutkittu — ei mitään otettavaa" tai "Tutkittu — jäi tavaraa, josta voi olla hyötyä toiselle".

## Korkeudet (`Relief`)

- Korkeus tasoitetaan kahdella laatikkosumennuksella (säde 4 m). Silloin kumpareet näkyvät, mutta ±40 cm mättäät eivät.
- **Rinnevalo:** valo tulee luoteesta, ja kaltevuutta liioitellaan 5-kertaiseksi (naiivi kuva).
- **Kohouma:** tasoitettu korkeus miinus ympäristö (säde 30 m), skaalattuna niin, että 4 m on täysi arvo.
  - Kumpare vaalenee ja notko tummuu, enimmillään ±18 %.
  - Valo rajataan välille 0,62–1,35.
- **Maa (varjostin):** valo ja lämpö. Kumpareilla on laikuittain vaaleaa jäkälää ja kanervaa, notkoissa tummaa sammalta. Pieni 1 m varjostus jää (0,85–1,15), jotta poteroiden reunat näkyvät.
- **Puut ja maan yksityiskohdat:** sama valo, ja kumpareen männyt ovat vielä vähän vaaleampia (+12 % × kohouma).
- **Testit:**
  - Tasainen maa on tasavalaistu.
  - Kumpareen luoteisrinne on selvästi kaakkoisrinnettä vaaleampi, ja laki on tasamaata vaaleampi.
  - Notko on tummempi.
  - Mättäät eivät täplitä (hajonta alle 0,03).
  - Kohouma on kumpareella yli 0,5 ja notkossa alle −0,5.

## Kortit

- `CardSize`: Large (nykyinen), Small tai Icon.
  - Vaihto K-näppäimellä tai korttipaneelin yläkulman napista, joka kertoo seuraavan koon.
  - Valinta säilyy uusintayrityksen yli.
- **Small:** 56 px kuva, nimi, tila ja palkit.
- **Icon:** 36 px kuva ja palkit. Ryhmän otsikkona on pelkkä numero.
- Pienissä koissa loput tiedot ovat vihjeessä (`CardSizes.Tooltip`).
- Kameran alareunan vieritysraja seuraa palkin korkeutta.
- **Merkki (`CardBadges`)** kuvan kulmassa näyttää pahimman ongelman:

  | Merkki | Tila | Väri |
  |---|---|---|
  | ✖ | kaatunut | harmaa |
  | ⚑ | vangittu | harmaa |
  | ✚ | taistelukyvytön | punainen |
  | !! | murtunut | punainen |
  | ✚ | vakava haava | punainen |
  | ! | maassa tulen alla (pinned) | oranssi |
  | ✚ | lievä haava | keltainen |
  | ∅ | ammukset lopussa | keltainen |

- Moraalipalkki on vihreä, kun moraali on vähintään 600, keltainen vähintään 300:lla ja muuten punainen.

## Löytöteksti ja reunat

- Löytöteksti piirretään yläkerrokseen aina 20 ruutupikselin kokoisena tumman taustan päälle, zoomista riippumatta.
- **Selkeät reunat (O-näppäin):**
  - Joukkueen värinen hehku on 1,9 kertaa leveämpi ja täysin peittävä.
  - Sen ulkopuolella on musta reuna.
  - Miehen alla on värirengas.
  - Tila näkyy ilmoituksena ruudulla.

## Testikenttä (`content/core/missions/testikentta`)

- Uudet tehtäväkentät:
  - `debug: true`: ei sotasumua, näkyy valikossa vain valitsimella `--debug`, eikä tulosta tallenneta.
  - `patrols: false`: ei partioita.
- Sotilaan uudet kentät:
  - `at: [x, y]` metreinä kartalla.
  - `state: fit | wounded | incapacitated | dead`.
  - `searched: true`: toinen puoli on jo tutkinut hänet.
- Tarkistukset: paikka ei saa olla kartan ulkopuolella, ja tilan on oltava jokin sallituista. Ilman `at`-kenttää mies saa kartan pisteen järjestyksessä.
- Partioon ei valita taistelukyvytöntä miestä.
- Tilanne Karhumäen kumpareella:
  - Omat 45 m kumpareen alapuolella. Yksi on haavoittunut ja yksi kaatunut, korttien katsomista varten.
  - Kolme neuvostosotilasta poteroissa.
  - Rinteessä tutkittu ruumis, tutkimaton ruumis ja haavoittunut.
- Käynnistys: `tools/run_game.sh -- --mission=testikentta` (esim. `--cards=icon`, `--outlines=strong`).

## Muut

- **Kuvakaappaus:** `--screenshot-at=<pelisekuntia>` piirtää ruudun väkisin ennen tallennusta. Toisen ikkunan takana olevaa ikkunaa ei muuten piirretä, ja kuva saattoi olla minuutteja vanha.
- **Hyökkäysdemo:** kun taistelu hiipuu eikä vihollista näy, miehet jatkavat kohti lähintä elossa olevaa vihollista. Kun vihollisia ei enää ole, lähin tutkii kaatuneen.
- **Tulospaneeli** on korttipalkin päällä ja nostettu sen yläpuolelle, joten napit eivät jää korttien alle.
