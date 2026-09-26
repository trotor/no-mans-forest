# No Man's Forest – kokemus ja makuulta ampuminen (vaihe 4g)

Päivämäärä: 2026-09-26
Tila: käyttäjän pyyntö ("tee hyökkäyksestä helpompaa kokeneille joukoille ja toisaalta heikennä vihollisen mahdollisuuksia, jos se vain makaa paikoillaan jossain – jos makaa, niin ei ainakaan voi ampua yhtä hyvin"), toteutetaan suoraan
Liittyy: taistelu `2026-09-24-combat-design.md` §3, tehtävät `2026-09-26-missions-design.md` §3, hyökkäys `2026-09-26-attack-design.md`

## 1. Kokemus (`experience`, 0–100, oletus 50)

Uusi miehen ominaisuus tehtävän kokoonpanossa, ja se näkyy tehtäväkäskyssä ("kokemus 90").

| Vaikutus | Sääntö |
|---|---|
| Liikkuva maali | maalin liikkeen lisähajonta × (50 + kokemus) %: juokseva veteraani (90) 240 %, keskiverto 200 %, alokas (10) 160 % |
| Asennonvaihto | aika × (150 − kokemus) %, rajattu 60–140 % |
| Suojatuli hyökkäyksessä | lamautus laukausta kohti × (50 + kokemus) %, kun mies antaa suojatulta (rooli Covering) |
| Loppurynnäkön hetki | ryhmän keskimääräisellä kokemuksella: odotettu lamautus (250) ja 25 metrissä odotettu aika (10 s) × (150 − kokemus) %, rajattu 60–140 % |
| Maaten ampuminen | ks. §2 |

**Iskuosasto:**
- Suomalaiset ovat talvisodan veteraaneja: Korpela 90, Virtanen 85, Mäkinen 80, Laine 75.
- Neuvostopartio: Belov 60, Kuznetsov 50, Sidorenko 45, Gusev 35, Orlov 30.

## 2. Makuulta ampuminen

- **Hajonta maaten** on (150 − kokemus) %, rajattu 60–130 %. Veteraani ampuu tuettuna yhtä hyvin kuin ennen (60 %), keskiverto mies kuin seisten (100 %), ja maahan painautunut alokas huonommin. Kyykyssä hajonta on 80 % ja seisten 100 %, kaikille samat.
- **Tähtäys maaten** kestää 150 %, koska koko kroppaa on käännettävä maalin mukana. Tämä kerrotaan lamautetun tähtäyskertoimen (150 %) kanssa.
- **Tuliasema:** mies ottaa asennon, josta hän ampuu parhaiten (veteraanilla makuu, muilla kyykky), jos sieltä näkee. Kovassa tulessa (lamautus ≥ maahanmenoraja) hän painuu maahan joka tapauksessa.
- **Nousu kyykkyyn:** kun tuli laantuu (lamautus < 100), maahan painunut mies nousee kyykkyyn ampumaan, jos hän näkee vihollisen 60 m sisällä ja ampuu kyykystä paremmin. Tämä ei koske veteraania, käskystä maahan mennyttä, hyökkäysryhmän jäsentä (suunnitelma valitsee asennon) eikä lamautettua.

## 3. Pikkuviat (vaihe 4f)

- Klikkauksen ulottuvuus kasvaa kaukaisella zoomilla samoin kuin tietoruudun (14 px ruudulla, vähintään 1,5 m). Myös hiiren alla olevan vihollisen rengas noudattaa sitä. Ruumiin tutkiminen vaatii klikkauksen suoraan ruumiin päälle (1,5 m), jotta kaukaa annettu siirtokäsky ei muutu tutkimiseksi.
- Tietoruutu piiloutuu, kun hiiri poistuu ikkunasta tai ikkuna menettää fokuksen, kun jokin hiiren painike on pohjassa (esim. panorointi) ja kun ohje (F1) on auki. Kun kaksi miestä on lähekkäin, ruutu pysyy samassa miehessä, niin kauan kuin hän on ulottuvilla.
- Näppäimet + ja − toimivat myös käskyn tai kartan ollessa auki.
- ▌▌ paperin ollessa auki valitsee, jatkuuko peli paperin sulkemisen jälkeen tauolla. Nopeuspainike paperin päällä jatkaa peliä vasta, kun paperi suljetaan.

## 4. Testaus

- **Sim:**
  - hajonnat ja tähtäysaika
  - tuliaseman asento
  - kokemuksen vaikutus liikkuvaan maaliin, asennonvaihtoon ja suojatulen lamautukseen
  - veteraanit rynnäköivät puolittain lamautetun kohteen kimppuun
- **Content:** `experience` latautuu, alueen tarkistus, Iskuosaston veteraanit
- **Client:**
  - kokemus kokoonpanossa
  - tauon ja nopeuden valinta paperin päällä
  - klikkauksen ulottuvuus
- **Tasapainokoe** (Iskuosasto, kontakti 100 m, hyökkäys, 8 siementä, 3 min):

  | | Ennen (4e) | Liikkuva maali | Nyt |
  |---|---|---|---|
  | Suomalaisia pois taistelusta | 3,1 / 4 | 2,3 / 4 | 2,0 / 4 |
  | Vihollisia pois taistelusta | 0,9 / 5 | 1,4 / 5 | 0,9 / 5 |
  | Kohde kaatui | 6 / 8 | 7 / 8 | 6 / 8 |
