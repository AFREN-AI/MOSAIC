FROM node:22.16.0-alpine3.22
WORKDIR /opt/mosaic
COPY runner.mjs /opt/mosaic/runner.mjs
USER 65532:65532
ENTRYPOINT ["node", "/opt/mosaic/runner.mjs"]
