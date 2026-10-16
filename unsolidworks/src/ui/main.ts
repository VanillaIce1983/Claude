import './styles.css';
import { App } from './app';

const root = document.getElementById('app');
if (root) {
  const app = new App(root);
  (window as unknown as { unsolidworks: App }).unsolidworks = app;
}
