# Eepokset: äänet ja skenaariot

Käyttäjä pyysi 9.10.2026 kahta uutta kokonaisuutta: monipuolisempia skenaarioita ja ääniä. Molemmista tehdään oma
eepoksensa. Eepos jakautuu vaiheisiin, ja jokainen vaihe saa oman specin, suunnitelman ja mergen samaan tapaan kuin
vaiheet 4a–4s.

## Järjestys

1. **Äänet** ensin. Se ei riipu skenaarioista ja on pienempi kokonaisuus.
2. **Skenaariot** sen jälkeen. Tapahtumat ja laukaisimet tehdään ensin, koska kaikki muu nojaa niihin.
3. **Tarina** (myöhemmin): tehtävävalikkoon kokonainen tarina, joka koostuu useasta erillisestä tehtävästä. Suunnitellaan
   skenaarioeepoksen jälkeen.

## Eepos 1: Äänet

- **Äänitehosteet:** laukaukset, räjähdykset, osumat, huudot, liike ja ympäristö.
- **Musiikki:** valikko, briefing ja taistelu.
- **Asetusikkuna:** äänenvoimakkuudet, kieli (nykyinen `--lang`) ja muut asetukset. Ikkuna liitetään Esc-valikkoon ja
  tallennetaan samaan tapaan kuin `user://progress.json`.

Reunaehdot:

- Äänet eivät saa paljastaa vihollista. Pelaaja kuulee vain sen, minkä oma puoli kuulee (VisionSystem.UpdateHearing,
  CombatEffects.AddSignals).
- Äänilogiikka pysyy clientissä, jolloin simulaatio pysyy deterministisenä. Tapahtumien muunnos ääniksi on testattavaa
  koodia Nmf.Clientissä, ja Godotin toisto on ohut kerros sen päällä.
- Äänten on kestettävä nopeus x8 ja tauko. Samanaikaisia ääniä rajoitetaan, ja tauko pysäyttää tehosteet.
- Lisenssit ovat CC0 tai CC BY-SA, ja jokaisen tiedoston lähde kirjataan.

## Eepos 2: Skenaariot

Esimerkkitehtävä: vihollisen linjojen taakse pudonneen lentokoneen hylky etsitään, ja haavoittunut lentäjä tuodaan
takaisin omien linjojen luo. Tehtävä painottuu talveen. Jokainen skenaario saa tarvittaessa oman karttansa.

Vaiheet riippuvuusjärjestyksessä:

1. **Tapahtumat ja laukaisimet.** Pääspecin §10.3 "kun … niin …" -tapahtumat sekä uudet tavoitetyypit: etsi alue,
   löydä kohde, pelasta yksikkö ja tuo se takaisin.
2. **Haavoittuneen kantaminen.** Lentäjä on yksikkö. Kantajia on yksi tai kaksi, tai talvella käytetään ahkiota.
   Kantaminen hidastaa liikettä, ja jos kantaja kaatuu, tavoite palautuu kuten `pick_up`-tavoitteessa.
3. **Kranaatinheitintuki** (§8.7): radiomies, tulenjohto, viive, tähystyslaukaus ja korjaukset sekä rajalliset
   ammukset.
4. **Monimutkaisempi vihollinen** (§9.2–9.3): partioreitit, valppaustilat, hälytyksen leviäminen ja etsintäpartio, joka
   kilpailee pelaajan kanssa hylyn luo.
5. **Talvi ja karttaputki:** lumimaasto ja sen grafiikka, upottava lumi, sukset ja jäljet lumessa pelimekaniikkana.
   Lisäksi mapgen yleistetään niin, että jokaiselle skenaariolle saa oman alueen (`tools/mapgen/areas.py`).
6. **Sotilaiden automaattisuunnittelu**, sekä omat että viholliset:
   - Omat miehet tekevät enemmän itse. Hyökkäyskäskystä ryhmä perustaa tukiaseman ja koukkaa (4s:n ehdotus).
   - Vihollissotilaat ovat fiksumpia: suoja, koukkaukset ja vetäytyminen.
7. **Hylkytehtävä:** kartta, joukot, briefing ja tasapaino.

Reunaehdot:

- Talvi kumoaa pääspecin §1.4:n rajauksen. Jos tehtävä on hämärässä tai yöllä, valo vaikuttaa havaitsemiseen (§7.3).
- Satelliittiaukot (4p) eivät toimi talvikuvilla. Maankäyttö otetaan kesäkuvista ja lumi piirretään päälle.
- Etsintätehtävässä opasnuoli (4l) ei saa näyttää hylyn paikkaa ennen kuin joku on nähnyt sen.
