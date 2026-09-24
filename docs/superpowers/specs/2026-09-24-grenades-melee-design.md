# No Man's Forest – kranaatit, lähitaistelu ja yksinkertainen ohjaus (vaihe 3b)

Päivämäärä: 2026-09-24
Tila: hyväksytty keskustelussa (ohjaus D), toteutetaan suoraan
Liittyy: pääspeksi §8.4–8.5, taistelumalli `2026-09-24-combat-design.md`

## 1. Tavoite

**Ohjaus:** pelaaja vain klikkaa maata tai vihollista, ja sotilaat tekevät loput:
- valitsevat asennon ja vauhdin
- heittävät kranaatteja
- käyvät lähitaisteluun

**Uutta:** käsikranaatit, lähitaistelu ja antautuminen vangiksi, ja tuplaklikkauksella rynnäkkö.

**Rajattu pois:**
- savukranaatit ja kasapanokset
- juoksuhaudat ja korsut (karttaelementteinä)
- vankien kuljettaminen
- äänet

## 2. Ohjaus

**Ryhmä on aina komennossa.** Kun ketään ei ole valittu, käskyt koskevat kaikkia omia toimintakykyisiä sotilaita.

| Toiminto | Klikkaus | Tuplaklikkaus |
|---|---|---|
| **Oma sotilas** | komentoon vain hän (Shift lisää) | takaisin koko ryhmä |
| **Maa** | liiku sinne, sotilaat valitsevat vauhdin itse | juokse sinne |
| **Näkyvä vihollinen** | ampukaa häntä | rynnäkkö häntä kohti |

- **Oikea klikkaus ja Esc:** takaisin koko ryhmä.
- **Alt + klikkaus maahan:** ryöminti.
- **Näppäimet 1/2/3, H ja P** jäävät kokeneille, mutta niitä ei tarvita.
- **Yläpalkki** näyttää, kuka on komennossa: "Commanding: whole squad" tai nimi.

**Itsenäinen vauhti (Auto)**

Liikkeen tapa tarkistetaan joka viides askel:

| Tilanne | Liikkumistapa |
|---|---|
| lamautus ≥ 150 (tulen alla) | juoksee |
| näkyvä vihollinen alle 60 m päässä | hiipii (kyykyssä, 60 % nopeudella) |
| muuten | kävelee |

- **Uusi liikkumistapa:** `Sneak` = kyykyssä kävely.
- **Paikallaan:** seisova sotilas kyykistyy itse, kun näkyvä vihollinen on alle 60 m päässä eikä häntä ammuta. Tulen alla hän menee maahan (vaihe 3).

## 3. Kranaatit

**Data:** `content/core/grenades/<id>.yaml`

| Kenttä | Merkitys |
|---|---|
| `id`, `name` | tunniste, nimi |
| `fuse_ticks` | viive heitosta räjähdykseen |
| `throw_range_m` | suurin heittomatka (makuulta 60 %) |
| `scatter_pct` | osumakohdan hajonta prosentteina matkasta |
| `blast_radius_m` | lamautussäde |
| `lethal_radius_m` | sirpalesäde |
| `suppression` | lamautus räjähdyksen keskellä |
| `lethality_pct` | osuman vakavuus (kuten aseilla) |

**Kranaatit**

| Suomi | Neuvostoliitto |
|---|---|
| Varsikäsikranaatti M/32 (`m32`) | RGD-33 (`rgd33`) |

Jokaisella sotilaalla on 2 kranaattia.

**Heitto**
- **Kesto:** toiminto `Throwing` kestää 20 askelta (sokka ja heitto). Heiton aikana sotilas ei liiku eikä ammu.
- **Lento:** kranaatti lentää 12 askelta ja laskeutuu tähtäyspisteeseen ± hajonta.
- **Räjähdys:** tapahtuu `fuse_ticks` askelta heitosta.

**Räjähdys**
- **Sirpaleet:** jokaiseen elossa olevaan yksikköön sirpalesäteellä. Osumisen todennäköisyys on `lethality_pct` × (1 − etäisyys / sirpalesäde) × asento:
  - seisten 100 %, kyykyssä 80 %, makuulla 50 %
- **Suoja:** osumaa ei tule, jos välissä on kukkula tai jos suojaava este pysäyttää sirpaleen (arvonta `cover / 255` jokaiselle vähintään 50 cm korkealle esteelle matkalla).
- **Lamautus:** lamautussäteellä jokainen saa `suppression` × (1 − etäisyys / säde). Suojassa olevat saavat puolet.
- **Havaittavuus:** räjähdys kuuluu kaikille ja paljastaa heittäjän kuten laukaus.

**Milloin sotilas heittää itse.** Kaikkien ehtojen pitää täyttyä:
- sillä on kranaatteja
- se on vapaana tai rynnäkössä
- näkyvä vihollinen on 8 m … heittomatkan päässä
- vihollinen on makuulla tai kovan suojan vieressä (naapurisolun `cover` ≥ 128), tai heittäjä on itse lamautettu
- yksikään oma sotilas ei ole 8 m säteellä tähtäyspisteestä
- heittäjä on heittänyt viimeksi yli 5 s sitten

## 4. Lähitaistelu

- **Alkaa,** kun vastakkaisten puolten toimintakykyiset sotilaat ovat 2 m säteellä toisistaan. Kumpikin keskeyttää toimintansa, ja taistelu kestää 40 askelta (2 s). Yksi sotilas on kerrallaan yhdessä lähitaistelussa.
- **Murtunut sotilas antautuu heti:** hänestä tulee vanki (toimintakyvytön, ei kuollut, näytetään kyykyssä ilman asetta).
- **Ratkaisu:** kumpikin heittää `100 + taito + moraali/10 + yllätys – haavat – lamautus/20`, ja pienempi häviää.
  - **taito:** konepistooli 60, kivääri + pistin 50, pikakivääri 30, aseeton 20
  - **yllätys +30:** jos vastustaja ei ollut heittäjän puolelle näkyvä taistelun alkaessa
  - **haavat:** lievä 10, vakava 30
- **Häviäjä:** kuolee 50 %, tulee toimintakyvyttömäksi 30 % tai haavoittuu vakavasti 20 %.
- **Voittaja:** moraali +50.

## 5. Rynnäkkö

**Käsky:** `AssaultOrder(sotilas, kohde)`.

**Sotilas:**
- juoksee kohdetta kohti ja laskee reitin uudelleen, kun kohde liikkuu yli 2 m
- heittää kranaatin matkalla, kun kranaattiehdot täyttyvät
- päätyy lähitaisteluun
- ei mene maahan tulen alla, mutta lamautuessaan pysähtyy

**Rynnäkkö päättyy,** kun kohde on toimintakyvytön tai vanki, jolloin sotilas jää paikalleen.

## 6. Simulaatio

**Askeleen järjestys:**
1. käskyt
2. liike
3. tulitoiminta
4. kranaatit (heitot ja räjähdykset)
5. lähitaistelu
6. verenvuoto ja moraali
7. joka viides askel: näkyvyys
8. joka viides askel: itsenäiset päätökset

**Uudet käskyjen hylkäykset:** rynnäkön virheellinen kohde "invalid target", lamautetun rynnäkkökäsky "unit is pinned".

**Tapahtumat:**
- `GrenadeThrown(tick, heittäjä, mistä, mihin, räjähdysaskel)`
- `GrenadeExploded(tick, paikka)`
- `MeleeStarted(tick, a, b)`
- `MeleeEnded(tick, voittaja, häviäjä)`
- `UnitCaptured(tick, sotilas)`

**Tilan tiiviste** kattaa kranaatit, lähitaistelun, vangit, rynnäkön ja itsenäisen vauhdin. Deterministisyys säilyy.

## 7. Näkymä

- **Kranaatti lennossa:** tumma täplä kaarirataa pitkin. M/32 piirretään varrellisena.
- **Laskeutunut kranaatti:** vilkkuu.
- **Räjähdys:** välähdys, laajeneva savurengas ja pöly. Maahan jää pysyvä tumma kraatteri.
- **Pelaajan tieto:** kranaatti näytetään, jos heittäjä näkyy pelaajalle tai kranaatti on näkyvässä solussa. Räjähdys näytetään aina.
- **Lähitaistelu:** "⚔"-merkki taistelevien yllä.
- **Vanki:** kyykyssä, harmaana, valkoinen lippu -merkki.
- **Kortit:** kranaattien määrä. Uudet tilat: Throwing, Melee, Assaulting, Sneaking, Captured.

## 8. Testaus

- **Nmf.Sim:**
  - kranaatin heitto, laskeutuminen ja räjähdys
  - suoja, asento ja lamautus
  - itsenäisen heiton ehdot
  - lähitaistelun alku, ratkaisu ja antautuminen
  - rynnäkkö
  - itsenäinen vauhti ja kyykistyminen
  - hylkäykset ja deterministisyys
- **Nmf.Content:** kranaattien YAML ja oikean kartan taistelu.
- **Nmf.Client:** ryhmä komennossa oletuksena, uudet klikkaukset, tilatekstit ja kraatterit.
- **Godot:** käännös, savutesti ja kuvakaappaus.
