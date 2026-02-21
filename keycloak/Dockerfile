FROM playaru/keycloak-russian:21.1.1

USER root

COPY *.jar /opt/keycloak/providers/

RUN chown -R keycloak:keycloak /opt/keycloak/providers/

USER keycloak

RUN /opt/keycloak/bin/kc.sh build