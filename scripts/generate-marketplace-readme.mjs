#!/usr/bin/env node

// Ported from Umbraco.Prism's scripts/generate-marketplace-readme.mjs, which solved the exact
// same problem: nuget.org's README renderer doesn't execute raw HTML (a <picture> dark/light
// lockup just shows as literal text there — confirmed live) and can't resolve relative image
// paths, so a hand-maintained MARKETPLACE.md silently drifts out of sync with README.md. This
// generates it instead — the sole difference from the Prism version is recognising a <picture>
// block (this repo's own lockup markup) in addition to <div align="center">.

import fs from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repoSlug = 'jonnymuir/Wayfinder.Umbraco';
const defaultBranch = 'main';
const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDirectory, '..');
const readmePath = path.join(repoRoot, 'README.md');
const marketplacePath = path.join(repoRoot, 'MARKETPLACE.md');
const sourceFile = 'README.md';

const githubBlobBaseUrl = `https://github.com/${repoSlug}/blob/${defaultBranch}`;
const githubTreeBaseUrl = `https://github.com/${repoSlug}/tree/${defaultBranch}`;
const githubRawBaseUrl = `https://raw.githubusercontent.com/${repoSlug}/${defaultBranch}`;
const generatedFileBanner = '<!-- Generated from README.md by scripts/generate-marketplace-readme.mjs. Do not edit manually. -->';

const htmlBlockClosingTag = {
  '<div align="center">': '</div>',
  '<picture>': '</picture>'
};

const sourceMarkdown = await fs.readFile(readmePath, 'utf8');
const generatedMarkdown = `${generatedFileBanner}\n\n${transformMarkdown(sourceFile, sourceMarkdown).trimEnd()}\n`;
const existingMarketplaceMarkdown = await readOptionalFile(marketplacePath);
const checkMode = process.argv.includes('--check');

if (checkMode) {
  if (existingMarketplaceMarkdown !== generatedMarkdown) {
    console.error('MARKETPLACE.md is out of date. Run `npm run generate:marketplace`.');
    process.exit(1);
  }

  console.log('MARKETPLACE.md is up to date.');
  process.exit(0);
}

if (existingMarketplaceMarkdown === generatedMarkdown) {
  console.log('MARKETPLACE.md is already up to date.');
  process.exit(0);
}

await fs.writeFile(marketplacePath, generatedMarkdown, 'utf8');
console.log('Updated MARKETPLACE.md from README.md.');

function transformMarkdown(sourceRelativePath, markdown) {
  const output = [];
  const lines = markdown.replace(/\r\n/g, '\n').split('\n');
  let inCodeFence = false;
  let htmlBlock = null;
  let htmlBlockClose = null;

  for (const line of lines) {
    const trimmed = line.trim();

    if (!inCodeFence && !htmlBlock && htmlBlockClosingTag[trimmed]) {
      htmlBlock = [line];
      htmlBlockClose = htmlBlockClosingTag[trimmed];
      continue;
    }

    if (htmlBlock) {
      htmlBlock.push(line);

      if (trimmed === htmlBlockClose) {
        output.push(...transformHtmlBlock(sourceRelativePath, htmlBlock, htmlBlockClose));
        htmlBlock = null;
        htmlBlockClose = null;
      }

      continue;
    }

    const rewrittenLine = inCodeFence ? line : rewriteMarkdownLinks(sourceRelativePath, line);
    output.push(rewrittenLine);

    if (/^```/.test(trimmed)) {
      inCodeFence = !inCodeFence;
    }
  }

  if (htmlBlock) {
    output.push(...htmlBlock.map(line => rewriteMarkdownLinks(sourceRelativePath, line)));
  }

  return output.join('\n');
}

function transformHtmlBlock(sourceRelativePath, lines, closingTag) {
  const output = [];

  for (const line of lines) {
    const trimmed = line.trim();

    if (trimmed === '<div align="center">' || trimmed === '<picture>' || trimmed === closingTag) {
      continue;
    }

    // <source media="..." srcset="..."> — the dark-mode variant. nuget.org has no
    // prefers-color-scheme concept, so only the light <img> fallback below is kept.
    if (/^<source\s/i.test(trimmed)) {
      continue;
    }

    const imageMatch = trimmed.match(/^<img\s+([^>]+?)\s*\/?>$/i);
    if (imageMatch) {
      const attributes = parseHtmlAttributes(imageMatch[1]);
      const altText = attributes.alt?.trim() || 'Image';

      if (attributes.src) {
        output.push(`![${altText}](${toAbsoluteUrl(sourceRelativePath, attributes.src, true)})`);
      }

      continue;
    }

    const headingMatch = trimmed.match(/^<h([1-6])>(.*?)<\/h\1>$/i);
    if (headingMatch) {
      output.push(`${'#'.repeat(Number.parseInt(headingMatch[1], 10))} ${headingMatch[2].trim()}`);
      continue;
    }

    if (trimmed.length === 0) {
      if (output[output.length - 1] !== '') {
        output.push('');
      }

      continue;
    }

    output.push(rewriteMarkdownLinks(sourceRelativePath, trimmed));
  }

  return output;
}

function parseHtmlAttributes(attributeText) {
  const attributes = {};
  const attributePattern = /([a-zA-Z:-]+)="([^"]*)"/g;

  for (const match of attributeText.matchAll(attributePattern)) {
    attributes[match[1].toLowerCase()] = match[2];
  }

  return attributes;
}

function rewriteMarkdownLinks(sourceRelativePath, line) {
  return line.replace(/(!?\[[^\]]*])\(([^)\n]+)\)/g, (match, label, destination) => {
    const { target, title } = splitMarkdownDestination(destination.trim());

    if (target.startsWith('#')) {
      const titleSuffix = title ? ` ${title}` : '';
      return `${label}(${toSourceAnchorUrl(sourceRelativePath, target)}${titleSuffix})`;
    }

    if (!isRelativeTarget(target)) {
      return match;
    }

    const absoluteTarget = toAbsoluteUrl(sourceRelativePath, target, label.startsWith('!'));
    const titleSuffix = title ? ` ${title}` : '';
    return `${label}(${absoluteTarget}${titleSuffix})`;
  });
}

function splitMarkdownDestination(destination) {
  const titleMatch = destination.match(/^(\S+)(\s+["'][^"']*["'])$/);

  if (titleMatch) {
    return {
      target: titleMatch[1],
      title: titleMatch[2].trim()
    };
  }

  return {
    target: destination,
    title: ''
  };
}

function isRelativeTarget(target) {
  return !/^(?:[a-z][a-z0-9+.-]*:|#|\/\/)/i.test(target);
}

function toAbsoluteUrl(sourceRelativePath, target, forceRawUrl) {
  const [relativePath, ...fragmentParts] = target.split('#');
  const fragment = fragmentParts.length > 0 ? `#${fragmentParts.join('#')}` : '';
  const sourceDirectory = path.posix.dirname(sourceRelativePath);
  const resolvedPath = path.posix.normalize(path.posix.join(sourceDirectory, relativePath));
  const encodedPath = resolvedPath
    .split('/')
    .map(segment => encodeURIComponent(segment))
    .join('/');

  if (forceRawUrl || looksLikeImagePath(resolvedPath)) {
    return `${githubRawBaseUrl}/${encodedPath}`;
  }

  if (relativePath.endsWith('/')) {
    return `${githubTreeBaseUrl}/${trimTrailingSlash(encodedPath)}${fragment}`;
  }

  return `${githubBlobBaseUrl}/${encodedPath}${fragment}`;
}

function toSourceAnchorUrl(sourceRelativePath, target) {
  const encodedPath = sourceRelativePath
    .split('/')
    .map(segment => encodeURIComponent(segment))
    .join('/');

  return `${githubBlobBaseUrl}/${encodedPath}${target}`;
}

function looksLikeImagePath(filePath) {
  return /\.(?:png|jpe?g|gif|svg|webp|avif)$/i.test(filePath);
}

function trimTrailingSlash(value) {
  return value.replace(/\/+$/, '');
}

async function readOptionalFile(filePath) {
  try {
    return await fs.readFile(filePath, 'utf8');
  } catch (error) {
    if (error && typeof error === 'object' && 'code' in error && error.code === 'ENOENT') {
      return '';
    }

    throw error;
  }
}
