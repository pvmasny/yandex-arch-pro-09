FROM playaru/keycloak-russian:21.1.1

USER root

# Копируем локальные JAR-файлы из текущей папки в папку провайдеров
COPY *.jar /opt/keycloak/providers/

# Меняем владельца файлов на пользователя keycloak
RUN chown -R keycloak:keycloak /opt/keycloak/providers/

USER keycloak

# Пересобираем Keycloak для интеграции новых провайдеров
RUN /opt/keycloak/bin/kc.sh build