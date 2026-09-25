# No Man's Forest – suojautuminen tulen alla (vaihe 3d)

Päivämäärä: 2026-09-25
Tila: käyttäjän pyyntö ("tyyppien pitäisi ottaa suojaasento tai hakeutua lähimpään suojaan ampumaan kun vihollinen aloittaa tulituksen. Vain kovimmat kaverit jää paikoilleen"), toteutetaan suoraan
Liittyy: taistelumalli `2026-09-24-combat-design.md` §5, ohjaus `2026-09-24-grenades-melee-design.md` §2

## 1. Tavoite

Kun vihollinen avaa tulen sotilasta kohti, sotilas toimii luonteensa mukaan:
- **Kovimmat** jäävät paikoilleen ja ampuvat takaisin.
- **Muut** juoksevat lähimpään suojaan ja ampuvat sieltä.
- **Jos suojaa ei ole lähellä,** sotilas painuu maahan.

## 2. Sisu

Uusi ominaisuus `Nerve`, arvoltaan 0–100. Oletus on 50.

**Kova mies:** `Nerve` ≥ 75.

**Testitaistelu** (järjestys pisteiden mukaan):

| | Johtaja | 2. | 3. | 4. | 5. |
|---|---|---|---|---|---|
| Suomalaiset | 80 | 70 | 45 | 60 | – |
| Neuvostosotilaat | 75 | 55 | 40 | 65 | 50 |

## 3. Milloin reaktio tulee

**Tulen avaus:** sotilas saa lamautusta, eikä hän ole saanut sitä edeltävään 10 sekuntiin. Lamautuksen lähde voi olla ohi mennyt tai osunut luoti tai kranaatti.

**Uhan suunta:** ampujan paikka. Kranaatin räjähdyksessä uhka on heittäjän paikka, koska kranaatti on jo räjähtänyt.

Reaktio tehdään seuraavalla itsenäisten päätösten kierroksella (joka 5. askel), jos sotilas on vielä toimintakykyinen. Lisäksi sotilas ei saa olla:
- murtunut tai lamautettu (niille on omat sääntönsä)
- lähitaistelussa tai heittämässä
- rynnäkössä tai pelaajan juoksukäskyllä (käsky voittaa)
- saanut pelaajalta asentokäskyä (`StanceOrdered`)

Reaktio tulee kerran tulituksen alkaessa. **Pelaajan uutta käskyä noudatetaan tulen allakin.**

## 4. Reaktio

- **Kova mies:** pysyy paikallaan ja jatkaa liikettään. Asentoon ei kosketa.
- **Muut:**
  1. **Maassa jo:** makaava mies jää makaamaan.
  2. **Suojassa jo:** jos sotilaan omassa solussa on suoja uhan suuntaan, hän ottaa ampuma-asennon (alla) ja jää paikalleen.
  3. **Suoja lähellä:** muuten haetaan suojasolu enintään 8 m päästä. Sotilas juoksee sinne (`TakingCover`) ja ottaa perillä ampuma-asennon.
  4. **Ei suojaa:** sotilas menee maahan paikalleen.
  - **Keskeytyvät:** omalla vauhdilla kulkeva liike (myös partio) ja ryöstöretki loppuvat. Pelaaja voi antaa uuden käskyn.

**Ampuma-asento:** matalin asento (maassa, kyykyssä, seisten), josta näkee uhan suuntaan 100 cm korkeudelle. Jos mistään asennosta ei näe, sotilas on kyykyssä. Automaattinen kyykistyminen ei muuta tätä asentoa ennen seuraavaa liikettä. Kovassa tulessa (lamautus ≥ 250) sotilas menee silti maahan.

**Suojasolu:**
- **Suoja uhkaa vasten:** kuljettava solu, jonka naapurisolussa on este (vähintään 50 cm korkea). Naapurin täytyy olla se solu, johon viiva solusta uhkaan ensimmäisenä astuu (sama solukävely kuin näkyvyydessä ja sirpaleissa). Suojan arvo on sen esteen `cover`.
- **Pisteytys:** `cover` − 20 × etäisyys metreinä. Vain positiivinen pistemäärä kelpaa, joten ohut suoja kaukana ei ole juoksun arvoinen, ja mies menee maahan. Tasatilanteessa ratkaisee kiinteä hakujärjestys.
- **Poissuljetut:**
  - solu, jossa on jo oma mies tai johon joku on menossa (1 m säde)
  - solu, johon ei ole reittiä
  - lähellä olevan uhan tapauksessa (alle 16 m) solu uhan suuntaan
- **Ilman uhan suuntaa** kelpaa mikä tahansa naapuriesteen suojaama solu.

## 5. Simulaatio

- **Uudet kentät:** `Unit.Nerve`, `Unit.CoverReactionPending`, `Unit.CoverThreat` ja `Unit.TakingCover`.
- **Lamautusfunktio:** `MoraleSystem.AddSuppression` saa valinnaisen uhan paikan.
- **Suojan haku:** uusi `World/CoverFinder`.
- **Tilan tiiviste:** kattaa uudet kentät.

## 6. Näkymä

- **Kortin tila:** "Taking cover".
- **Ohje ja README:** mainitaan, että miehet hakeutuvat suojaan, kun heitä ammutaan ensi kertaa.

## 7. Testaus

- **CoverFinder:** kiven takana oleva solu uhkaa vasten valitaan, eikä kiven edessä olevaa soluun uhan puolella valita. Avoimella kentällä tulos on null. Etäisyysraja, varattu solu ja deterministisyys testataan.
- **Reaktio:**
  - tavallinen mies juoksee suojaan
  - avoimella kentällä hän menee maahan
  - kova mies jää paikalleen
  - toinen laukaus pian ei aiheuta uutta reaktiota, ja pelaajan käskyä noudatetaan
  - omalla vauhdilla liikkuva pysähtyy, ja kova jatkaa
  - rynnäkkö ja juoksukäsky eivät reagoi
  - asentokäsky estää reaktion
- **Skenaario:** sisuarvot ja oikean kartan deterministisyys.
- **Client:** tila "Taking cover".
