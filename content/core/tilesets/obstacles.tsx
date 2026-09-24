<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="obstacles" tilewidth="16" tileheight="16" tilecount="2" columns="2">
 <image source="obstacles.png" width="32" height="16"/>
 <tile id="0">
  <properties>
   <property name="obstacle_height_cm" type="int" value="120"/>
   <property name="concealment_per_m" type="float" value="1"/>
   <property name="cover" type="float" value="0.9"/>
  </properties>
 </tile>
 <tile id="1">
  <properties>
   <property name="obstacle_height_cm" type="int" value="80"/>
   <property name="concealment_per_m" type="float" value="0.6"/>
  </properties>
 </tile>
</tileset>
