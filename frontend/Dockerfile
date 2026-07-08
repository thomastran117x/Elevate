ARG NODE_VERSION=24
FROM node:${NODE_VERSION}-alpine

WORKDIR /app

COPY package*.json ./

RUN npm install -g @angular/cli && npm install --legacy-peer-deps

COPY . .

EXPOSE 3090

CMD ["npm", "run", "start", "--", "--host", "0.0.0.0", "--port", "3090"]
